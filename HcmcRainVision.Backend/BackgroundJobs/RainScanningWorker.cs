using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries; // Thư viện xử lý bản đồ
using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;
using HcmcRainVision.Backend.Services.AI;
using HcmcRainVision.Backend.Services.Crawling;
using HcmcRainVision.Backend.Services.ImageProcessing;
using HcmcRainVision.Backend.Services.Notification; // Thêm using này

namespace HcmcRainVision.Backend.BackgroundJobs
{
    public class RainScanningWorker : BackgroundService
    {
        private readonly ILogger<RainScanningWorker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IWebHostEnvironment _env;

        public RainScanningWorker(
            ILogger<RainScanningWorker> logger, 
            IServiceProvider serviceProvider,
            IWebHostEnvironment env)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _env = env;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Tạo thư mục lưu ảnh nếu chưa có: wwwroot/images/rain_logs
            string webRootPath = _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            string saveFolder = Path.Combine(webRootPath, "images", "rain_logs");
            
            if (!Directory.Exists(saveFolder))
            {
                Directory.CreateDirectory(saveFolder);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                try
                {
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        // 1. Lấy các Service cần thiết
                        var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();
                        var processor = scope.ServiceProvider.GetRequiredService<IImagePreProcessor>();
                        var aiService = scope.ServiceProvider.GetRequiredService<RainPredictionService>();
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                        // 2. Lấy danh sách Camera từ Database (Thay vì hardcode)
                        var cameras = await dbContext.Cameras.ToListAsync(stoppingToken);

                        if (cameras.Count == 0)
                        {
                            _logger.LogWarning("⚠️ Không tìm thấy camera nào trong Database!");
                        }

                        foreach (var cam in cameras)
                        {
                            // --- BƯỚC 1: CRAWL ---
                            byte[]? rawBytes = await crawler.FetchImageAsync(cam.SourceUrl);
                            if (rawBytes == null || rawBytes.Length == 0) continue;

                            // --- BƯỚC 2: PRE-PROCESS ---
                            // Cắt và resize ảnh
                            byte[]? processedBytes = processor.ProcessForAI(rawBytes);
                            if (processedBytes == null) continue;

                            // --- BƯỚC 3: AI DETECT ---
                            var prediction = aiService.Predict(processedBytes);

                            // --- BƯỚC 4: LOGIC LƯU ẢNH (Chỉ lưu khi có mưa để tiết kiệm ổ cứng) ---
                            string? savedImageUrl = null;
                            
                            if (prediction.IsRaining)
                            {
                                // Tạo tên file unique: CAM_ID_TimeStamp.jpg
                                string fileName = $"{cam.Id}_{DateTime.Now.Ticks}.jpg";
                                string fullPath = Path.Combine(saveFolder, fileName);

                                // Lưu file đã xử lý (processedBytes) để nhẹ hơn
                                await File.WriteAllBytesAsync(fullPath, processedBytes, stoppingToken);

                                // Đường dẫn để Frontend truy cập
                                savedImageUrl = $"/images/rain_logs/{fileName}";
                            }

                            // --- BƯỚC 5: LƯU LOG VÀO DB ---
                            var weatherLog = new WeatherLog
                            {
                                CameraId = cam.Id,
                                Timestamp = DateTime.UtcNow,
                                IsRaining = prediction.IsRaining,
                                Confidence = prediction.Confidence,
                                Location = new Point(cam.Longitude, cam.Latitude) { SRID = 4326 },
                                ImageUrl = savedImageUrl // Lưu đường dẫn vào đây
                            };

                            dbContext.WeatherLogs.Add(weatherLog);
                            
                            _logger.LogInformation($"✅ Saved: {cam.Name} | Mưa: {prediction.IsRaining} ({prediction.Confidence*100:0}%) | Img: {savedImageUrl}");

                            // --- BƯỚC 6: GỬI EMAIL CẢNH BÁO (Nếu có mưa và độ tin cậy cao) ---
                            // TẠM GIẢM NGƯỠNG XUỐNG 0.7 (70%) ĐỂ TEST EMAIL
                            if (prediction.IsRaining && prediction.Confidence > 0.7)
                            {
                                string subject = $"⚠️ CẢNH BÁO MƯA: Phát hiện tại Camera {cam.Name}";
                                string body = $@"
                                    <h3>Hệ thống HCMC Rain Vision phát hiện mưa!</h3>
                                    <p><b>Camera:</b> {cam.Name} ({cam.Id})</p>
                                    <p><b>Thời gian:</b> {DateTime.Now}</p>
                                    <p><b>Độ tin cậy:</b> {prediction.Confidence * 100:0.00}%</p>
                                    <p>Vui lòng mang theo áo mưa hoặc tìm nơi trú ẩn.</p>
                                    <hr/>
                                    <small>Đây là email tự động.</small>
                                ";

                                // Gửi mail (Thay email này bằng email thật của bạn để test)
                                await emailService.SendEmailAsync("khaivpmse184623@fpt.edu.vn", subject, body);
                            }
                        }

                        // Commit transaction
                        await dbContext.SaveChangesAsync(stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Worker Error: {ex.Message}");
                }

                // Nghỉ 5 phút
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}