using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;
using HcmcRainVision.Backend.Services.AI;
using HcmcRainVision.Backend.Services.Crawling;
using HcmcRainVision.Backend.Services.ImageProcessing;
using HcmcRainVision.Backend.Services.Notification;
using HcmcRainVision.Backend.Models.Enums;
using HcmcRainVision.Backend.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace HcmcRainVision.Backend.BackgroundJobs
{
    public class RainScanningWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RainScanningWorker> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<RainHub> _hubContext;

        // Thay bool bằng SemaphoreSlim để lock an toàn hơn
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        
        // Biến để theo dõi lần chạy cleanup cuối cùng
        private DateTime _lastCleanupTime = DateTime.MinValue;

        public RainScanningWorker(IServiceProvider serviceProvider, ILogger<RainScanningWorker> logger, IWebHostEnvironment env, IHubContext<RainHub> hubContext)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _env = env;            _hubContext = hubContext;        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            // 1. Dọn dẹp các Job bị treo do lần tắt server trước
            await ResetStuckJobsAsync();
            await base.StartAsync(cancellationToken);
        }

        private async Task ResetStuckJobsAsync()
        {
            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var stuckJobs = await db.IngestionJobs
                    .Where(j => j.Status == nameof(JobStatus.Running))
                    .ToListAsync();

                if (stuckJobs.Any())
                {
                    foreach (var job in stuckJobs)
                    {
                        job.Status = nameof(JobStatus.Failed);
                        job.Notes = "System restart/crash while running";
                        job.EndedAt = DateTime.UtcNow;
                    }
                    await db.SaveChangesAsync();
                    _logger.LogWarning($"Đã dọn dẹp {stuckJobs.Count} job bị treo.");
                }
            }
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Rain Scanning Worker starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                // Thử wait lock trong 0ms (kiểm tra xem có ai đang chạy không)
                if (!await _lock.WaitAsync(0))
                {
                    _logger.LogWarning("⚠️ Job cũ chưa chạy xong. Bỏ qua lượt này.");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                    continue;
                }

                Guid jobId = Guid.NewGuid();

                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

                        // Tạo Job Log
                        var job = new IngestionJob { JobId = jobId, JobType = "Scheduled", Status = nameof(JobStatus.Running), StartedAt = DateTime.UtcNow };
                        db.IngestionJobs.Add(job);
                        await db.SaveChangesAsync();

                        // Lấy danh sách Stream đang Active
                        var streams = await db.CameraStreams
                            .Include(s => s.Camera)
                                .ThenInclude(c => c.Ward)
                            .Where(s => s.IsActive && s.Camera.Status != nameof(CameraStatus.Maintenance))
                            .ToListAsync(stoppingToken);

                        _logger.LogInformation($"Đã tải {streams.Count} CameraStream cần quét.");

                        // TỐI ƯU N+1: Load tất cả subscriptions RA NGOÀI vòng lặp
                        var activeSubscriptions = await db.AlertSubscriptions
                            .Include(s => s.User)
                            .Include(s => s.Ward)
                            .Where(s => s.IsEnabled && s.WardId != null)
                            .ToListAsync(stoppingToken);

                        // Gom nhóm theo WardId để tra cứu nhanh O(1)
                        var subsByWard = activeSubscriptions
                            .GroupBy(s => s.WardId!)
                            .ToDictionary(g => g.Key, g => g.ToList());

                        _logger.LogInformation($"Đã tải {activeSubscriptions.Count} subscriptions từ {subsByWard.Count} phường.");

                        // Xử lý song song (Max 5 camera cùng lúc)
                        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = stoppingToken };

                        await Parallel.ForEachAsync(streams, parallelOptions, async (stream, token) =>
                        {
                            await ProcessCameraAsync(stream, jobId, scope.ServiceProvider, subsByWard, token);
                        });

                        // Kết thúc Job
                        job.Status = nameof(JobStatus.Completed);
                        job.EndedAt = DateTime.UtcNow;
                        job.Notes = $"Processed {streams.Count} streams";
                        await db.SaveChangesAsync();
                        
                        _logger.LogInformation($"✅ Hoàn thành Job #{jobId}");
                        
                        // SỬA LỖI HIỆU NĂNG: Chỉ Cleanup 1 lần mỗi ngày
                        if (DateTime.UtcNow.Day != _lastCleanupTime.Day)
                        {
                            await CleanupOldImagesAsync();
                            await CleanupOldDataAsync(db, stoppingToken);
                            _lastCleanupTime = DateTime.UtcNow;
                            _logger.LogInformation("🧹 Đã chạy cleanup hàng ngày.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Critical error in RainScanningWorker");
                }
                finally
                {
                    _lock.Release(); // Giải phóng lock
                }

                // Chờ 5 phút
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        private async Task ProcessCameraAsync(CameraStream stream, Guid jobId, IServiceProvider services, Dictionary<string, List<AlertSubscription>> subsByWard, CancellationToken token)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();
            var aiService = scope.ServiceProvider.GetRequiredService<IRainPredictionService>();
            var firebaseService = scope.ServiceProvider.GetRequiredService<IFirebasePushService>();
            var cloudService = scope.ServiceProvider.GetRequiredService<ICloudStorageService>();
            var preProcessor = scope.ServiceProvider.GetRequiredService<IImagePreProcessor>();

            var attempt = new IngestionAttempt { AttemptId = Guid.NewGuid(), JobId = jobId, CameraId = stream.CameraId, AttemptAt = DateTime.UtcNow };
            var attemptStartTime = DateTime.UtcNow;

            try
            {
                // 1. Crawl ảnh
                byte[]? imageBytes = await crawler.FetchImageAsync(stream.StreamUrl);
                double latencyMs = (DateTime.UtcNow - attemptStartTime).TotalMilliseconds;
                
                if (imageBytes == null)
                {
                    attempt.Status = nameof(AttemptStatus.Failed);
                    attempt.ErrorMessage = "Connection Timeout";
                    attempt.LatencyMs = (int)latencyMs;
                    
                    // Log offline
                    var statusLog = new CameraStatusLog
                    {
                        CameraId = stream.CameraId,
                        Status = nameof(CameraStatus.Offline),
                        CheckedAt = DateTime.UtcNow,
                        Reason = "Connection Timeout"
                    };
                    db.CameraStatusLogs.Add(statusLog);
                    
                    // Update Camera Status -> Offline
                    var camera = await db.Cameras.FindAsync(new object[] { stream.CameraId }, token);
                    if (camera != null)
                    {
                        camera.Status = nameof(CameraStatus.Offline);
                    }
                }
                else
                {
                    attempt.Status = nameof(AttemptStatus.Success);
                    attempt.HttpStatus = 200;
                    attempt.LatencyMs = (int)latencyMs;
                    
                    // --- HASH CHECK: Phát hiện camera bị treo (ảnh giống hệt lần trước) ---
                    using var md5 = MD5.Create();
                    var hashBytes = md5.ComputeHash(imageBytes);
                    var currentHash = Convert.ToHexString(hashBytes);

                    // Lấy thông tin camera để check hash cũ
                    var currentCamera = await db.Cameras.FindAsync(new object[] { stream.CameraId }, token);
                    
                    if (currentCamera != null && currentCamera.LastImageHash == currentHash)
                    {
                        _logger.LogWarning($"📷 Camera {stream.CameraId} ({stream.Camera.Name}) bị treo - ảnh giống hệt lần trước. Bỏ qua xử lý AI.");
                        
                        // Log stuck camera status
                        var stuckLog = new CameraStatusLog
                        {
                            CameraId = stream.CameraId,
                            Status = "Stuck", // TODO: Thêm CameraStatus.Stuck vào enum
                            CheckedAt = DateTime.UtcNow,
                            Reason = "Duplicate image hash detected"
                        };
                        db.CameraStatusLogs.Add(stuckLog);
                        attempt.ErrorMessage = "Stuck camera - duplicate image";
                        
                        db.IngestionAttempts.Add(attempt);
                        await db.SaveChangesAsync(token);
                        return; // Dừng xử lý camera này
                    }

                    // Cập nhật hash mới (EF Core change tracking sẽ tự update)
                    if (currentCamera != null)
                    {
                        currentCamera.LastImageHash = currentHash;
                    }
                    // ----------------------------------------------------------------
                    
                    // 2. XỬ LÝ ẢNH TRƯỚC KHI ĐƯA VÀO AI
                    // Resize về 224x224, cắt bỏ timestamp và logo thừa
                    var processedImageBytes = preProcessor.ProcessForAI(imageBytes);
                    
                    if (processedImageBytes == null)
                    {
                        _logger.LogWarning($"❌ Không thể xử lý ảnh từ camera {stream.CameraId}. Bỏ qua.");
                        attempt.Status = nameof(AttemptStatus.Failed);
                        attempt.ErrorMessage = "Image processing failed";
                        db.IngestionAttempts.Add(attempt);
                        await db.SaveChangesAsync(token);
                        return;
                    }
                    
                    // 3. AI Dự báo (Sử dụng ảnh đã xử lý để tăng độ chính xác)
                    var prediction = aiService.Predict(processedImageBytes);
                    bool isRainingNow = prediction.IsRaining;

                    // 4. ⚡ TỐI ƯU LƯU TRỮ: CHỈ LƯU ẢNH KHI CÓ MƯA HOẶC CONFIDENCE THẤP
                    // Tiết kiệm > 90% dung lượng Cloud/Local storage
                    string? imageUrl = null;
                    
                    if (isRainingNow || prediction.Confidence < 0.6)
                    {
                        string fileName = $"{stream.CameraId}_{DateTime.UtcNow.Ticks}.jpg";
                        imageUrl = await cloudService.UploadImageAsync(imageBytes, fileName); // Lưu ảnh GỐC đẹp, không phải ảnh đã resize

                        if (string.IsNullOrEmpty(imageUrl))
                        {
                            // Fallback: Lưu Local nếu Cloudinary lỗi hoặc chưa config
                            string localPath = Path.Combine(_env.WebRootPath, "images", "rain_logs", fileName);
                            Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                            await File.WriteAllBytesAsync(localPath, imageBytes, token);
                            imageUrl = $"/images/rain_logs/{fileName}";
                        }
                        
                        _logger.LogInformation($"💾 Đã lưu ảnh: {fileName} (Mưa: {isRainingNow}, Confidence: {prediction.Confidence:P0})");
                    }
                    else
                    {
                        _logger.LogDebug($"⏭️ Bỏ qua lưu ảnh camera {stream.CameraId} (Không mưa, Confidence cao: {prediction.Confidence:P0})");
                    }

                    // 5. LOGIC CHỐNG SPAM NÂNG CAO
                    // Lấy log mưa gần nhất của camera này
                    var lastRainLog = await db.WeatherLogs
                        .Where(l => l.CameraId == stream.CameraId && l.IsRaining)
                        .OrderByDescending(l => l.Timestamp)
                        .FirstOrDefaultAsync(token);
                    
                    // Chỉ gửi thông báo nếu:
                    // 1. Hiện tại đang mưa
                    // 2. VÀ (Chưa từng mưa HOẶC Lần mưa cuối cách đây hơn 30 phút) -> Cooldown 30p
                    bool shouldNotify = isRainingNow && 
                                        (lastRainLog == null || (DateTime.UtcNow - lastRainLog.Timestamp).TotalMinutes > 30);

                    if (shouldNotify)
                    {
                        // Gửi Firebase Push Notification (tối ưu với Dictionary)
                        await SendNotificationsOptimizedAsync(stream, prediction.Confidence, subsByWard, firebaseService);
                        
                        // GỬI SIGNALR (REAL-TIME CHO WEB) - Gửi theo Group Quận
                        var alertData = new 
                        {
                            CameraId = stream.CameraId,
                            CameraName = stream.Camera.Name,
                            WardName = stream.Camera.Ward?.WardName,
                            DistrictName = stream.Camera.Ward?.DistrictName,
                            ImageUrl = imageUrl,
                            Confidence = prediction.Confidence,
                            Timestamp = DateTime.UtcNow
                        };

                        // Gửi cho Group Dashboard (tổng hợp)
                        await _hubContext.Clients.Group("Dashboard").SendAsync("ReceiveRainAlert", alertData, token);
                        
                        // GỬi cho Group Quận cụ thể (chuẩn hóa tên)
                        if (!string.IsNullOrEmpty(stream.Camera.Ward?.DistrictName))
                        {
                            var normalizedDistrictName = NormalizeGroupName(stream.Camera.Ward.DistrictName);
                            await _hubContext.Clients.Group(normalizedDistrictName).SendAsync("ReceiveRainAlert", alertData, token);
                            _logger.LogDebug($"📡 Gửi SignalR tới group: {normalizedDistrictName}");
                        }
                        
                        _logger.LogInformation($"📡 Đã gửi SignalR alert cho camera {stream.Camera.Name}");
                    }

                    // 6. Lưu Log Kết quả
                    var weatherLog = new WeatherLog
                    {
                        CameraId = stream.CameraId,
                        IsRaining = isRainingNow,
                        Confidence = prediction.Confidence,
                        ImageUrl = imageUrl, // Dùng URL từ Cloudinary hoặc Local
                        Timestamp = DateTime.UtcNow,
                        // Lưu ý: Gán Location từ Camera vào WeatherLog
                        Location = new NetTopologySuite.Geometries.Point(stream.Camera.Longitude, stream.Camera.Latitude) { SRID = 4326 }
                    };
                    db.WeatherLogs.Add(weatherLog);
                    
                    // Log online
                    var statusLog = new CameraStatusLog
                    {
                        CameraId = stream.CameraId,
                        Status = nameof(CameraStatus.Active),
                        CheckedAt = DateTime.UtcNow
                    };
                    db.CameraStatusLogs.Add(statusLog);
                    
                    // Update Camera Status -> Active
                    var camera = await db.Cameras.FindAsync(new object[] { stream.CameraId }, token);
                    if (camera != null)
                    {
                        camera.Status = nameof(CameraStatus.Active);
                    }
                }
            }
            catch (Exception ex)
            {
                attempt.Status = nameof(AttemptStatus.Error);
                attempt.ErrorMessage = ex.Message;
                _logger.LogError(ex, $"Lỗi xử lý Camera {stream.CameraId}");
            }

            db.IngestionAttempts.Add(attempt);
            await db.SaveChangesAsync(token);
        }

        // Helper chuyển đổi giờ VN
        private string GetVietnamTime(DateTime utcTime)
        {
            TimeZoneInfo vnTimeZone = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            DateTime vnTime = TimeZoneInfo.ConvertTimeFromUtc(utcTime, vnTimeZone);
            return vnTime.ToString("HH:mm dd/MM/yyyy");
        }

        /// <summary>
        /// Chuẩn hóa tên Quận/Phường cho SignalR Group (loại bỏ dấu, khoảng trắng)
        /// Ví dụ: "Quận 1" -> "quan_1", "Bình Thạnh" -> "binh_thanh"
        /// </summary>
        private string NormalizeGroupName(string? name)
        {
            if (string.IsNullOrEmpty(name)) return "unknown";
            
            return name
                .ToLowerInvariant()
                .Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => char.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
                .Aggregate(new System.Text.StringBuilder(), (sb, c) => sb.Append(c))
                .ToString()
                .Replace(" ", "_")
                .Replace("-", "_");
        }

        private async Task SendNotificationsOptimizedAsync(
            CameraStream stream, 
            float confidence, 
            Dictionary<string, List<AlertSubscription>> subsByWard,
            IFirebasePushService firebase)
        {
            if (stream.Camera.WardId == null || !subsByWard.ContainsKey(stream.Camera.WardId)) return;

            var subscriptions = subsByWard[stream.Camera.WardId];
            string timeStr = DateTime.UtcNow.AddHours(7).ToString("HH:mm"); // Giờ VN cứng

            foreach (var sub in subscriptions)
            {
                // Kiểm tra ngưỡng tin cậy tại bộ nhớ
                if (confidence >= sub.ThresholdProbability && !string.IsNullOrEmpty(sub.User.DeviceToken))
                {
                    // Fire and forget - không chặn luồng chính
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await firebase.SendToDeviceAsync(
                                sub.User.DeviceToken, 
                                "Cảnh báo mưa! 🌧️", 
                                $"Mưa tại {stream.Camera.Name} lúc {timeStr}"
                            );
                            _logger.LogInformation($"📱 Đã gửi push notification cho {sub.User.Email}");
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, $"Lỗi gửi push notification cho {sub.User.Email}");
                        }
                    });
                }
            }
        }

        // Tự động xóa ảnh cũ quá 24h
        private async Task CleanupOldImagesAsync()
        {
            try
            {
                var folderPath = Path.Combine(_env.WebRootPath, "images", "rain_logs");
                var dir = new DirectoryInfo(folderPath);
                if (dir.Exists)
                {
                    await Task.Run(() =>
                    {
                        foreach (var file in dir.GetFiles())
                        {
                            if (file.CreationTimeUtc < DateTime.UtcNow.AddHours(-24))
                            {
                                file.Delete();
                            }
                        }
                    });
                    _logger.LogInformation("🧹 Đã dọn dẹp ảnh cũ hơn 24 giờ.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cleanup old images");
            }
        }
        
        private async Task CleanupOldDataAsync(AppDbContext db, CancellationToken token)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-7);

                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM ingestion_attempts WHERE attempt_at < {0}",
                    cutoffDate
                );

                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM ingestion_jobs WHERE started_at < {0}",
                    cutoffDate
                );

                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM camera_status_logs WHERE checked_at < {0}",
                    cutoffDate
                );

                _logger.LogInformation("🧹 Đã dọn dẹp dữ liệu cũ hơn 7 ngày.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi cleanup old data");
            }
        }
    }
}
