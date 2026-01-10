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
                CleanupOldImages(saveFolder);

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
                        // Xử lý song song
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

                                if (prediction.IsRaining)
                                {
                                    // Chỉ lưu ảnh nếu đang mưa
                                    string fileName = $"{cam.Id}_{DateTime.Now.Ticks}.jpg";
                                    string fullPath = Path.Combine(saveFolder, fileName);
                                    await File.WriteAllBytesAsync(fullPath, processedBytes, token);
                                    savedImageUrl = $"/images/rain_logs/{fileName}";

                                    if (shouldAlert)
                                    {
                                        // 1. Gửi SignalR
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

                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }

        private void CleanupOldImages(string folderPath)
        {
            try 
            {
                var cutoff = DateTime.Now.AddDays(-7);
                var files = Directory.GetFiles(folderPath);
                foreach (var file in files)
                {
                    var fi = new FileInfo(file);
                    if (fi.CreationTime < cutoff)
                    {
                        fi.Delete();
                    }
                }
            }
            catch {}
        }
    }
}