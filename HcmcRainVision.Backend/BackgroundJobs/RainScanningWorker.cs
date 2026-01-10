using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.SignalR;
using NetTopologySuite.Geometries; // Thư viện xử lý bản đồ
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
                    // 1. Lấy danh sách Camera trước (Dùng scope tạm để lấy list)
                    List<Camera> cameras;
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        cameras = await dbContext.Cameras.ToListAsync(stoppingToken);
                    }

                    if (cameras.Count == 0)
                    {
                        _logger.LogWarning("⚠️ Không tìm thấy camera nào trong Database!");
                    }
                    else
                    {
                        _logger.LogInformation($"🚀 Bắt đầu xử lý {cameras.Count} camera song song...");

                        // 2. Xử lý song song (Giới hạn tối đa 5 request cùng lúc để không bị chặn IP)
                        var parallelOptions = new ParallelOptions 
                        { 
                            MaxDegreeOfParallelism = 5, 
                            CancellationToken = stoppingToken 
                        };

                        await Parallel.ForEachAsync(cameras, parallelOptions, async (cam, token) =>
                        {
                            try
                            {
                                // QUAN TRỌNG: Tạo Scope MỚI cho mỗi luồng chạy song song
                                using var scope = _serviceProvider.CreateScope();
                                var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();
                                var processor = scope.ServiceProvider.GetRequiredService<IImagePreProcessor>();
                                var aiService = scope.ServiceProvider.GetRequiredService<RainPredictionService>();
                                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                                var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

                                // --- BƯỚC 1: CRAWL ---
                                byte[]? rawBytes = await crawler.FetchImageAsync(cam.SourceUrl);
                                if (rawBytes == null || rawBytes.Length == 0)
                                {
                                    _logger.LogWarning($"⚠️ Không crawl được ảnh từ camera {cam.Name}");
                                    return;
                                }

                                // --- BƯỚC 2: PRE-PROCESS ---
                                byte[]? processedBytes = processor.ProcessForAI(rawBytes);
                                if (processedBytes == null)
                                {
                                    _logger.LogWarning($"⚠️ Lỗi xử lý ảnh từ camera {cam.Name}");
                                    return;
                                }

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
                                    await File.WriteAllBytesAsync(fullPath, processedBytes, token);

                                    // Đường dẫn để Frontend truy cập
                                    savedImageUrl = $"/images/rain_logs/{fileName}";

                                    // --- GỬ8I THÔNG BÁO REAL-TIME QUA SIGNALR ---
                                    try
                                    {
                                        await _hubContext.Clients.All.SendAsync("ReceiveRainAlert", new
                                        {
                                            CameraId = cam.Id,
                                            CameraName = cam.Name,
                                            Latitude = cam.Latitude,
                                            Longitude = cam.Longitude,
                                            ImageUrl = savedImageUrl,
                                            Confidence = prediction.Confidence,
                                            Time = DateTime.Now
                                        }, token);

                                        _logger.LogInformation($"📡 Đã gửi SignalR alert cho camera {cam.Name}");
                                    }
                                    catch (Exception signalREx)
                                    {
                                        _logger.LogError($"⚠️ Lỗi gửi SignalR: {signalREx.Message}");
                                    }
                                }

                                // --- BƯỚC 5: LƯU LOG VÀO DB ---
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
                                await dbContext.SaveChangesAsync(token);

                                _logger.LogInformation($"✅ [{cam.Id}] {cam.Name} | Mưa: {prediction.IsRaining} ({prediction.Confidence * 100:0}%) | Img: {savedImageUrl}");

                                // --- BƯỚC 6: GỬI EMAIL CẢNH BÁO (Nếu có mưa và độ tin cậy cao) ---
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

                                    await emailService.SendEmailAsync("khaivpmse184623@fpt.edu.vn", subject, body);
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError($"❌ Lỗi xử lý camera {cam.Id} ({cam.Name}): {ex.Message}");
                            }
                        });

                        _logger.LogInformation($"✅ Hoàn thành xử lý {cameras.Count} camera");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ Worker Error: {ex.Message}");
                }

                // Nghỉ 5 phút
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}