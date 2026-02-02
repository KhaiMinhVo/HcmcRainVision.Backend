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

                // --- 1. DỌN DẸP ẢNH CŨ (Tự động xóa ảnh quá 7 ngày để không đầy ổ cứng) ---
                await CleanupOldData(stoppingToken);

                try
                {
                    // Lấy danh sách ID camera để xử lý (chỉ lấy ID để tránh lỗi tracking)
                    List<string> cameraIds;
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        cameraIds = await dbContext.Cameras.Select(c => c.Id).ToListAsync(stoppingToken);
                    }

                    if (cameraIds.Count == 0)
                    {
                        _logger.LogWarning("⚠️ Không tìm thấy camera nào trong Database!");
                    }
                    else
                    {
                        // 🚀 XỬ LÝ SONG SONG
                        // MaxDegreeOfParallelism = 5: Xử lý tối đa 5 cameras cùng lúc
                        // - Tăng lên 10-15 nếu server mạnh và có nhiều cameras
                        // - Giảm xuống 2-3 nếu server yếu hoặc bandwidth hạn chế
                        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 5, CancellationToken = stoppingToken };

                        await Parallel.ForEachAsync(cameraIds, parallelOptions, async (camId, token) =>
                        {
                            try
                            {
                                using var scope = _serviceProvider.CreateScope();
                                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                
                                // QUAN TRỌNG: Load lại Camera trong scope này để EF Core Tracking hoạt động
                                var cam = await dbContext.Cameras.FindAsync(new object[] { camId }, token);
                                if (cam == null) return;

                                var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();
                                var processor = scope.ServiceProvider.GetRequiredService<IImagePreProcessor>();
                                var aiService = scope.ServiceProvider.GetRequiredService<RainPredictionService>();
                                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                                // --- BƯỚC 1: CRAWL ---
                                byte[]? rawBytes = await crawler.FetchImageAsync(cam.SourceUrl);
                                if (rawBytes == null || rawBytes.Length == 0) return;

                                // --- BƯỚC 2: PRE-PROCESS ---
                                byte[]? processedBytes = processor.ProcessForAI(rawBytes);
                                if (processedBytes == null) return;

                                // --- BƯỚC 3: AI DETECT ---
                                var prediction = aiService.Predict(processedBytes);

                                // --- BƯỚC 4: LOGIC XỬ LÝ KẾT QUẢ ---
                                string? savedImageUrl = null;
                                
                                // Kiểm tra xem có nên gửi thông báo không (Chống SPAM)
                                // Logic: Chưa gửi bao giờ HOẶC đã quá 30 phút
                                bool shouldAlert = cam.LastRainAlertSent == null || 
                                                   (DateTime.UtcNow - cam.LastRainAlertSent.Value).TotalMinutes > 30;

                                // --- CẢI TIẾN: Lưu ảnh để retrain model (False negative detection) ---
                                bool isUnsure = prediction.Confidence > 0.4f && prediction.Confidence < 0.6f;
                                bool randomSample = new Random().Next(0, 100) < 5; // 5% xác suất lưu mẫu ngẫu nhiên
                                bool shouldSaveImage = prediction.IsRaining || isUnsure || randomSample;

                                if (shouldSaveImage)
                                {
                                    // Lưu ảnh cho training/debugging
                                    string fileName = $"{cam.Id}_{DateTime.UtcNow.Ticks}.jpg";
                                    
                                    // 1. Thử upload lên Cloudinary trước
                                    var cloudStorage = scope.ServiceProvider.GetRequiredService<ICloudStorageService>();
                                    var cloudinaryUrl = await cloudStorage.UploadImageAsync(processedBytes, fileName);
                                    
                                    if (!string.IsNullOrEmpty(cloudinaryUrl))
                                    {
                                        // Thành công → Dùng URL từ Cloudinary
                                        savedImageUrl = cloudinaryUrl;
                                        _logger.LogInformation($"☁️ Đã upload lên Cloudinary: {cloudinaryUrl}");
                                    }
                                    else
                                    {
                                        // Fallback → Lưu local nếu Cloudinary không khả dụng
                                        string fullPath = Path.Combine(saveFolder, fileName);
                                        await File.WriteAllBytesAsync(fullPath, processedBytes, token);
                                        savedImageUrl = $"/images/rain_logs/{fileName}";
                                        _logger.LogWarning($"⚠️ Cloudinary không khả dụng, lưu local: {savedImageUrl}");
                                    }

                                    // Log lý do lưu ảnh
                                    if (isUnsure)
                                        _logger.LogInformation($"💾 Lưu ảnh uncertain ({prediction.Confidence:0.00}) cho {cam.Id}");
                                    else if (randomSample && !prediction.IsRaining)
                                        _logger.LogInformation($"💾 Lưu ảnh sample ngẫu nhiên (no rain) cho {cam.Id}");
                                }

                                if (prediction.IsRaining)
                                {
                                    if (shouldAlert)
                                    {
                                        // 1. Gửi SignalR (đã có await)
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

                                        // 2. Gửi Email (chỉ gửi khi tin cậy cao > 70%)
                                        if (prediction.Confidence > 0.7)
                                        {
                                            string subject = $"⚠️ CẢNH BÁO MƯA: {cam.Name}";
                                            string body = $"<p>Phát hiện mưa tại <b>{cam.Name}</b> lúc {DateTime.Now}</p><p>Độ tin cậy: {prediction.Confidence*100:0}%</p>";
                                            // Không await để tránh block luồng xử lý chính
                                            _ = emailService.SendEmailAsync("khaivpmse184623@fpt.edu.vn", subject, body);
                                        }

                                        // 3. Cập nhật thời gian gửi để lần sau không gửi nữa
                                        cam.LastRainAlertSent = DateTime.UtcNow;
                                        // SaveChanges ở cuối sẽ lưu thay đổi này vào DB
                                        _logger.LogInformation($"📡 Đã gửi Alert cho {cam.Id}");
                                    }
                                }
                                else 
                                {
                                    // CẢI TIẾN: Nếu tạnh mưa, reset lại trạng thái để sẵn sàng báo cơn mưa mới ngay lập tức
                                    if (cam.LastRainAlertSent != null)
                                    {
                                        cam.LastRainAlertSent = null; 
                                        // Có thể gửi thêm 1 event SignalR báo "Đã tạnh mưa" nếu muốn Frontend hiển thị
                                        _logger.LogInformation($"🌤️ Đã tạnh mưa tại {cam.Id}, reset cảnh báo.");
                                    }
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
                                _logger.LogError($"❌ Lỗi camera {camId}: {ex.Message}");
                            }
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ Worker Error: {ex.Message}");
                }

                // ⏰ TẦN SUẤT QUÉT: 5 phút (Có thể điều chỉnh)
                // - Giảm xuống 2-3 phút để update nhanh hơn (khuyến nghị production)
                // - Tăng lên 10 phút để tiết kiệm bandwidth (development)
                // ⚠️ Lưu ý: Quét quá nhanh (< 1 phút) có thể bị server camera block
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        /// <summary>
        /// Dọn dẹp dữ liệu cũ: Xóa cả file ảnh VÀ record trong Database
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