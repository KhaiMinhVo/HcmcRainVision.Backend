using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;
using HcmcRainVision.Backend.Services.AI;
using HcmcRainVision.Backend.Services.Crawling;
using HcmcRainVision.Backend.Services.ImageProcessing;
using HcmcRainVision.Backend.Services.Notification;
using HcmcRainVision.Backend.Models.Enums;
using Microsoft.EntityFrameworkCore;

namespace HcmcRainVision.Backend.BackgroundJobs
{
    public class RainScanningWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RainScanningWorker> _logger;
        private readonly IWebHostEnvironment _env;

        // Thay bool bằng SemaphoreSlim để lock an toàn hơn
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);

        public RainScanningWorker(IServiceProvider serviceProvider, ILogger<RainScanningWorker> logger, IWebHostEnvironment env)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _env = env;
        }

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

                        // Xử lý song song (Max 5 camera cùng lúc)
                        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = stoppingToken };

                        await Parallel.ForEachAsync(streams, parallelOptions, async (stream, token) =>
                        {
                            await ProcessCameraAsync(stream, jobId, scope.ServiceProvider, token);
                        });

                        // Kết thúc Job
                        job.Status = nameof(JobStatus.Completed);
                        job.EndedAt = DateTime.UtcNow;
                        job.Notes = $"Processed {streams.Count} streams";
                        await db.SaveChangesAsync();
                        
                        _logger.LogInformation($"✅ Hoàn thành Job #{jobId}");
                        
                        // Dọn dẹp ảnh cũ
                        await CleanupOldImagesAsync();
                        
                        // Dọn dẹp logs cũ
                        await CleanupOldDataAsync(db, stoppingToken);
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

        private async Task ProcessCameraAsync(CameraStream stream, Guid jobId, IServiceProvider services, CancellationToken token)
        {
            using var scope = services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();
            var aiService = scope.ServiceProvider.GetRequiredService<RainPredictionService>();
            var firebaseService = scope.ServiceProvider.GetRequiredService<IFirebasePushService>();
            var cloudService = scope.ServiceProvider.GetRequiredService<ICloudStorageService>();

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
                    
                    // 2. AI Dự báo (Xử lý trước khi upload để tiết kiệm băng thông nếu cần)
                    var prediction = aiService.Predict(imageBytes);

                    // 3. Upload ảnh (Logic mới: Ưu tiên Cloudinary, Fallback về Local)
                    string fileName = $"{stream.CameraId}_{DateTime.UtcNow.Ticks}.jpg";
                    string? imageUrl = await cloudService.UploadImageAsync(imageBytes, fileName);

                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        // Fallback: Lưu Local nếu Cloudinary lỗi hoặc chưa config
                        string localPath = Path.Combine(_env.WebRootPath, "images", "rain_logs", fileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(localPath)!);
                        await File.WriteAllBytesAsync(localPath, imageBytes, token);
                        imageUrl = $"/images/rain_logs/{fileName}";
                    }

                    // 4. Logic Chống Spam Thông Báo (QUAN TRỌNG)
                    // Lấy log gần nhất của camera này để so sánh
                    var lastLog = await db.WeatherLogs
                        .Where(l => l.CameraId == stream.CameraId)
                        .OrderByDescending(l => l.Timestamp)
                        .FirstOrDefaultAsync(token);

                    bool isRainingNow = prediction.IsRaining;
                    bool wasRainingBefore = lastLog?.IsRaining ?? false;

                    // Chỉ gửi thông báo nếu: Hiện tại Mưa VÀ (Trước đó không mưa HOẶC Lần đầu tiên chạy)
                    if (isRainingNow && !wasRainingBefore)
                    {
                        await SendNotificationsAsync(stream, prediction.Confidence, db, firebaseService);
                    }

                    // 5. Lưu Log Kết quả
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

        private async Task SendNotificationsAsync(CameraStream stream, float confidence, AppDbContext db, IFirebasePushService firebase)
        {
            if (stream.Camera.WardId == null) return;

            // Tìm những user đăng ký phường này với độ tin cậy thấp hơn hoặc bằng kết quả AI
            var subscriptions = await db.AlertSubscriptions
                .Include(s => s.User)
                .Include(s => s.Ward)
                .Where(s => s.IsEnabled && s.WardId == stream.Camera.WardId && confidence >= s.ThresholdProbability)
                .ToListAsync();

            string timeStr = GetVietnamTime(DateTime.UtcNow);

            foreach (var sub in subscriptions)
            {
                // Gửi Firebase Push Notification
                if (!string.IsNullOrEmpty(sub.User.DeviceToken))
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
