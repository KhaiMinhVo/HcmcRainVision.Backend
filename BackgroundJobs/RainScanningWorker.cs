using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using NetTopologySuite.Geometries;
using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;
using HcmcRainVision.Backend.Services.AI;
using HcmcRainVision.Backend.Services.Crawling;
using HcmcRainVision.Backend.Services.ImageProcessing;
using HcmcRainVision.Backend.Services.Notification;
using HcmcRainVision.Backend.Hubs;

namespace HcmcRainVision.Backend.BackgroundJobs
{
    public class RainScanningWorker : BackgroundService
    {
        private readonly ILogger<RainScanningWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebHostEnvironment _env;
        private readonly IHubContext<RainHub> _hubContext;

        public RainScanningWorker(
            ILogger<RainScanningWorker> logger, 
            IServiceProvider serviceProvider,
            IWebHostEnvironment env,
            IHubContext<RainHub> hubContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _env = env;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            string webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            string saveFolder = Path.Combine(webRootPath, "images", "rain_logs");
            
            if (!Directory.Exists(saveFolder)) Directory.CreateDirectory(saveFolder);

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                // --- 1. CHỐNG CHỒNG CHÉO (OVERLAP PROTECTION) ---
                using (var scope = _serviceProvider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var isJobRunning = await db.IngestionJobs
                        .AnyAsync(j => j.Status == "Running" 
                                  && j.StartedAt > DateTime.UtcNow.AddMinutes(-10), stoppingToken);

                    if (isJobRunning)
                    {
                        _logger.LogWarning("⚠️ Job cũ chưa chạy xong. Bỏ qua lượt này.");
                        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                        continue;
                    }
                }

                // --- 2. TẠO INGESTION JOB MỚI ---
                Guid jobId = Guid.NewGuid();
                using (var scope = _serviceProvider.CreateScope())
                {
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    var job = new IngestionJob 
                    { 
                        JobId = jobId,
                        JobType = "RainScan",
                        Status = "Running",
                        StartedAt = DateTime.UtcNow 
                    };
                    db.IngestionJobs.Add(job);
                    await db.SaveChangesAsync();
                    _logger.LogInformation($"🚀 Bắt đầu Job quét #{jobId}");
                }

                try
                {
                    // Lấy danh sách STREAM thay vì Camera ID
                    List<CameraStream> activeStreams;
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        activeStreams = await dbContext.CameraStreams
                            .Include(s => s.Camera)
                            .ThenInclude(c => c.Ward)
                            .Where(s => s.IsActive && s.IsPrimary)
                            .ToListAsync(stoppingToken);
                    }

                    if (activeStreams.Count == 0)
                    {
                        _logger.LogWarning("⚠️ Không tìm thấy camera stream nào đang active!");
                    }
                    else
                    {
                        // 🚀 XỬ LÝ SONG SONG
                        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = stoppingToken };

                        await Parallel.ForEachAsync(activeStreams, parallelOptions, async (stream, token) =>
                        {
                            var attemptStartTime = DateTime.UtcNow;
                            string attemptStatus = "Success";
                            string? errorMessage = null;
                            int latencyMs = 0;
                            
                            try
                            {
                                using var scope = _serviceProvider.CreateScope();
                                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                
                                // Load lại Camera để có thể update status
                                var cam = await dbContext.Cameras.FindAsync(new object[] { stream.CameraId }, token);
                                if (cam == null) 
                                {
                                    attemptStatus = "Failed";
                                    errorMessage = "Camera not found";
                                    return;
                                }

                                var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();
                                var processor = scope.ServiceProvider.GetRequiredService<IImagePreProcessor>();
                                var aiService = scope.ServiceProvider.GetRequiredService<RainPredictionService>();
                                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                                // --- BƯỚC 1: CRAWL (Dùng StreamUrl từ bảng mới) ---
                                var crawlStartTime = DateTime.UtcNow;
                                byte[]? rawBytes = await crawler.FetchImageAsync(stream.StreamUrl);
                                latencyMs = (int)(DateTime.UtcNow - crawlStartTime).TotalMilliseconds;
                                
                                if (rawBytes == null || rawBytes.Length == 0) 
                                {
                                    attemptStatus = "Failed";
                                    errorMessage = "Failed to fetch image";
                                    
                                    // Cập nhật trạng thái camera thành Offline
                                    if (cam.Status != "Offline")
                                    {
                                        cam.Status = "Offline";
                                        dbContext.Cameras.Update(cam);
                                    }
                                    
                                    // Ghi log trạng thái Offline
                                    dbContext.CameraStatusLogs.Add(new CameraStatusLog 
                                    { 
                                        CameraId = cam.Id, 
                                        Status = "Offline",
                                        Reason = "Failed to fetch image",
                                        CheckedAt = DateTime.UtcNow 
                                    });
                                    await dbContext.SaveChangesAsync(token);
                                    return;
                                }
                                
                                // Camera hoạt động bình thường - cập nhật status Active
                                if (cam.Status != "Active")
                                {
                                    cam.Status = "Active";
                                    dbContext.Cameras.Update(cam);
                                }
                                
                                // Ghi log trạng thái Online
                                dbContext.CameraStatusLogs.Add(new CameraStatusLog 
                                { 
                                    CameraId = cam.Id, 
                                    Status = "Online",
                                    CheckedAt = DateTime.UtcNow 
                                });

                                // --- BƯỚC 2: PRE-PROCESS ---
                                byte[]? processedBytes = processor.ProcessForAI(rawBytes);
                                if (processedBytes == null) return;

                                // --- BƯỚC 3: AI DETECT ---
                                var prediction = aiService.Predict(processedBytes);

                                // --- BƯỚC 4: LOGIC XỬ LÝ KẾT QUẢ & GỬI THÔNG BÁO ---
                                string? savedImageUrl = null;
                                
                                // Kiểm tra xem có nên lưu ảnh không
                                bool isUnsure = prediction.Confidence > 0.4f && prediction.Confidence < 0.6f;
                                bool randomSample = new Random().Next(0, 100) < 5;
                                bool shouldSaveImage = prediction.IsRaining || isUnsure || randomSample;

                                if (shouldSaveImage)
                                {
                                    string fileName = $"{cam.Id}_{DateTime.UtcNow.Ticks}.jpg";
                                    var cloudStorage = scope.ServiceProvider.GetRequiredService<ICloudStorageService>();
                                    var cloudinaryUrl = await cloudStorage.UploadImageAsync(processedBytes, fileName);
                                    
                                    if (!string.IsNullOrEmpty(cloudinaryUrl))
                                    {
                                        savedImageUrl = cloudinaryUrl;
                                        _logger.LogInformation($"☁️ Đã upload lên Cloudinary: {cloudinaryUrl}");
                                    }
                                    else
                                    {
                                        string fullPath = Path.Combine(saveFolder, fileName);
                                        await File.WriteAllBytesAsync(fullPath, processedBytes, token);
                                        savedImageUrl = $"/images/rain_logs/{fileName}";
                                        _logger.LogWarning($"⚠️ Cloudinary không khả dụng, lưu local: {savedImageUrl}");
                                    }
                                }

                                if (prediction.IsRaining)
                                {
                                    // --- GỬI THÔNG BÁO BẰNG ALERTSUBSCRIPTION (MỚI) ---
                                    if (!string.IsNullOrEmpty(cam.WardId))
                                    {
                                        var subscriptions = await dbContext.AlertSubscriptions
                                            .Include(s => s.User)
                                            .ThenInclude(u => u.UserNotificationSettings)
                                            .Where(s => s.IsEnabled 
                                                     && s.WardId == cam.WardId 
                                                     && prediction.Confidence >= s.ThresholdProbability)
                                            .ToListAsync(token);

                                        if (subscriptions.Any())
                                        {
                                            _logger.LogInformation($"📡 Tìm thấy {subscriptions.Count} subscriptions cho Ward {cam.WardId}");
                                            
                                            // TODO: Implement Firebase notification
                                            // foreach (var sub in subscriptions)
                                            // {
                                            //     var deviceToken = sub.User.UserNotificationSettings.FirstOrDefault()?.DeviceToken;
                                            //     if (!string.IsNullOrEmpty(deviceToken))
                                            //     {
                                            //         await firebaseService.SendToDeviceAsync(deviceToken, "Mưa rồi!", $"Mưa tại {cam.Name}");
                                            //     }
                                            // }
                                        }
                                    }

                                    // Gửi SignalR
                                    await _hubContext.Clients.All.SendAsync("ReceiveRainAlert", new
                                    {
                                        CameraId = cam.Id,
                                        CameraName = cam.Name,
                                        Latitude = cam.Latitude,
                                        Longitude = cam.Longitude,
                                        ImageUrl = savedImageUrl,
                                        Confidence = prediction.Confidence,
                                        Time = DateTime.UtcNow
                                    }, token);

                                    // Gửi Email (confidence cao)
                                    if (prediction.Confidence > 0.7)
                                    {
                                        string subject = $"⚠️ CẢNH BÁO MƯA: {cam.Name}";
                                        string body = $"<p>Phát hiện mưa tại <b>{cam.Name}</b> lúc {DateTime.Now}</p><p>Độ tin cậy: {prediction.Confidence*100:0}%</p>";
                                        _ = emailService.SendEmailAsync("khaivpmse184623@fpt.edu.vn", subject, body);
                                    }

                                    _logger.LogInformation($"📡 Đã gửi Alert cho {cam.Id}");
                                }

                                // --- BƯỚC 5: LƯU LOG ---
                                var weatherLog = new WeatherLog
                                {
                                    CameraId = cam.Id,
                                    Timestamp = DateTime.UtcNow,
                                    IsRaining = prediction.IsRaining,
                                    Confidence = prediction.Confidence,
                                    Location = new Point(cam.Longitude, cam.Latitude) { SRID = 4326 },
                                    ImageUrl = savedImageUrl
                                };

                                dbContext.WeatherLogs.Add(weatherLog);
                                
                                // Lưu tất cả thay đổi (bao gồm update Camera và insert WeatherLog)
                                await dbContext.SaveChangesAsync(token);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"❌ Lỗi stream {stream.CameraId}: {ex.Message}");
                                attemptStatus = "Failed";
                                errorMessage = ex.Message;
                            }
                            finally
                            {
                                // --- GHI INGESTION ATTEMPT ---
                                try
                                {
                                    using var scope = _serviceProvider.CreateScope();
                                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                    
                                    var attempt = new IngestionAttempt
                                    {
                                        JobId = jobId,
                                        CameraId = stream.CameraId,
                                        Status = attemptStatus,
                                        LatencyMs = latencyMs,
                                        HttpStatus = attemptStatus == "Success" ? 200 : 500,
                                        ErrorMessage = errorMessage,
                                        AttemptAt = attemptStartTime
                                    };
                                    
                                    db.IngestionAttempts.Add(attempt);
                                    await db.SaveChangesAsync();
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogError($"⚠️ Không thể ghi IngestionAttempt: {ex.Message}");
                                }
                            }
                        });
                    }
                    
                    // --- 3. CẬP NHẬT JOB HOÀN TẤT ---
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var job = await db.IngestionJobs.FindAsync(jobId);
                        if (job != null)
                        {
                            job.Status = "Completed";
                            job.EndedAt = DateTime.UtcNow;
                            job.Notes = $"Processed {activeStreams.Count} camera streams";
                            await db.SaveChangesAsync();
                            _logger.LogInformation($"✅ Hoàn thành Job #{jobId}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ Worker Error: {ex.Message}");
                    
                    // Cập nhật Job thành Failed
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var job = await db.IngestionJobs.FindAsync(jobId);
                        if (job != null)
                        {
                            job.Status = "Failed";
                            job.EndedAt = DateTime.UtcNow;
                            job.Notes = $"Error: {ex.Message}";
                            await db.SaveChangesAsync();
                        }
                    }
                    catch { /* Ignore */ }
                }

                // --- 3. CLEANUP (DỌN DẸP DỮ LIỆU CŨ) ---
                await CleanupOldDataAsync(stoppingToken);

                // ⏰ TẦN SUẤT QUÉT: 5 phút (Có thể điều chỉnh)
                // - Giảm xuống 2-3 phút để update nhanh hơn (khuyến nghị production)
                // - Tăng lên 10 phút để tiết kiệm bandwidth (development)
                // ⚠️ Lưu ý: Quét quá nhanh (< 1 phút) có thể bị server camera block
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        /// <summary>
        /// Dọn dẹp dữ liệu cũ (Logs, Jobs, Status)
        /// Chỉ giữ dữ liệu trong 7 ngày để tránh database phình to
        /// </summary>
        private async Task CleanupOldDataAsync(CancellationToken token)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var threshold = DateTime.UtcNow.AddDays(-7);

                // Xóa Ingestion Attempts cũ
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM ingestion_attempts WHERE attempt_at < {0}", 
                    threshold);

                // Xóa Ingestion Jobs cũ
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM ingestion_jobs WHERE started_at < {0}", 
                    threshold);

                // Xóa Camera Status Logs cũ
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM camera_status_logs WHERE checked_at < {0}", 
                    threshold);

                _logger.LogInformation($"🧹 Đã dọn dẹp dữ liệu cũ hơn 7 ngày.");
            }
            catch (Exception ex)
            {
                _logger.LogError($"⚠️ Lỗi khi dọn dẹp dữ liệu: {ex.Message}");
            }
        }

        /// <summary>
        /// Dọn dẹp ảnh cũ và WeatherLog
        /// Xóa cả file ảnh VÀ record trong Database
        /// Đảm bảo đồng bộ giữa filesystem và DB để tránh lỗi 404
        /// </summary>
        private async Task CleanupOldData(CancellationToken token)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-7);

                // 1. Tìm các logs cũ có ảnh
                var oldLogs = await dbContext.WeatherLogs
                    .Where(x => x.Timestamp < cutoff && x.ImageUrl != null)
                    .ToListAsync(token);

                if (oldLogs.Count > 0)
                {
                    // 2. Xóa file trên đĩa (Tối ưu: xử lý theo batch để tránh treo Worker)
                    string webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
                    int deletedFiles = 0;
                    
                    // Chỉ xử lý 100 bản ghi mỗi lần để tránh quá tải
                    var logsToDelete = oldLogs.Take(100).ToList();

                    foreach (var log in logsToDelete)
                    {
                        if (!string.IsNullOrEmpty(log.ImageUrl))
                        {
                            // Chuyển URL relative thành đường dẫn tuyệt đối
                            // log.ImageUrl vd: "/images/rain_logs/abc.jpg" -> bỏ dấu / đầu
                            var filePath = Path.Combine(webRootPath, log.ImageUrl.TrimStart('/', '\\').Replace("/", Path.DirectorySeparatorChar.ToString()));

                            if (File.Exists(filePath))
                            {
                                try
                                {
                                    // Chạy xóa file ở luồng phụ để không block Worker
                                    await Task.Run(() => File.Delete(filePath), token);
                                    deletedFiles++;
                                }
                                catch (Exception ex)
                                {
                                    _logger.LogWarning($"⚠️ Không thể xóa file {filePath}: {ex.Message}");
                                }
                            }
                        }
                    }

                    // 3. Xóa records trong DB
                    dbContext.WeatherLogs.RemoveRange(logsToDelete);
                    await dbContext.SaveChangesAsync(token);

                    _logger.LogInformation($"🧹 Đã dọn dẹp {logsToDelete.Count} bản ghi cũ và {deletedFiles} file ảnh.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"⚠️ Lỗi khi dọn dẹp dữ liệu cũ: {ex.Message}");
            }
        }
    }
}