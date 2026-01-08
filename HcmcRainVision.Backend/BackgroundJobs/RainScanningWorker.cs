using HcmcRainVision.Backend.Services.Crawling;
using HcmcRainVision.Backend.Services.ImageProcessing;
using HcmcRainVision.Backend.Services.AI;

namespace HcmcRainVision.Backend.BackgroundJobs
{
    public class RainScanningWorker : BackgroundService
    {
        private readonly ILogger<RainScanningWorker> _logger;
        private readonly IServiceProvider _serviceProvider;

        public RainScanningWorker(ILogger<RainScanningWorker> logger, IServiceProvider serviceProvider)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);

                // Tạo Scope mới để gọi Database/Service (Bắt buộc trong Background Service)
               // Trong method ExecuteAsync của RainScanningWorker.cs

using (var scope = _serviceProvider.CreateScope())
{
    // Lấy service crawler ra
    var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();

    // Giả sử đây là danh sách URL camera (thực tế bạn sẽ lấy list này từ Database)
    var cameraUrls = new List<string> 
    { 
        "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=CAMERA_ID_THAT", // URL thật (ví dụ)
        "TEST_MODE_RAIN" // URL giả để test
    };

    foreach (var url in cameraUrls)
    {
        // Gọi hàm crawl
        byte[]? imageBytes = await crawler.FetchImageAsync(url);

        if (imageBytes != null && imageBytes.Length > 0)
        {
            _logger.LogInformation($"Đã tải ảnh thành công! Kích thước: {imageBytes.Length} bytes");

            // TODO: Bước tiếp theo - Gửi imageBytes này vào Service AI để dự đoán
            // var isRaining = await aiService.PredictRainAsync(imageBytes);
        }
    }
}
                // Nghỉ 5 phút trước khi chạy lại
using (var scope = _serviceProvider.CreateScope())
{
    var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();
    
    // Inject thêm PreProcessor
    var processor = scope.ServiceProvider.GetRequiredService<IImagePreProcessor>();

    var cameraUrls = new List<string> { "TEST_MODE" }; 

    foreach (var url in cameraUrls)
    {
        // Bước 1: Crawl
        byte[]? rawBytes = await crawler.FetchImageAsync(url);

        if (rawBytes != null && rawBytes.Length > 0)
        {
            // Bước 2: Xử lý ảnh (Cắt + Resize)
            byte[]? processedBytes = processor.ProcessForAI(rawBytes, 224, 224);

            if (processedBytes != null)
            {
                _logger.LogInformation($"Xử lý ảnh xong! Size gốc: {rawBytes.Length} -> Size mới: {processedBytes.Length}");
                
                // Lưu ý: Lúc này ảnh đã sạch, chỉ còn bầu trời và mặt đường, kích thước 224x224.
                // Sẵn sàng để đưa vào model ML.NET ở bước tiếp theo.
                
                // Demo lưu ảnh ra đĩa để bạn kiểm tra xem nó cắt đúng chưa
                await File.WriteAllBytesAsync($"processed_debug_{DateTime.Now.Ticks}.jpg", processedBytes);
            }
        }
    }
}



                using (var scope = _serviceProvider.CreateScope())
{
    var crawler = scope.ServiceProvider.GetRequiredService<ICameraCrawler>();
    var processor = scope.ServiceProvider.GetRequiredService<IImagePreProcessor>();
    var aiService = scope.ServiceProvider.GetRequiredService<RainPredictionService>(); // Lấy AI Service

    var cameraUrls = new List<string> { "TEST_MODE" }; 

    foreach (var url in cameraUrls)
    {
        // 1. Crawl
        byte[]? rawBytes = await crawler.FetchImageAsync(url);
        if (rawBytes == null) continue;

        // 2. Pre-process
        byte[]? processedBytes = processor.ProcessForAI(rawBytes);
        if (processedBytes == null) continue;

        // 3. AI Detect
        var prediction = aiService.Predict(processedBytes);

        // 4. Log kết quả (Sau này sẽ là Lưu vào DB)
        _logger.LogInformation($"📸 Camera: {url}");
        _logger.LogInformation($"🌧️ Kết quả: {(prediction.IsRaining ? "CÓ MƯA" : "TẠNH RÁO")}");
        _logger.LogInformation($"🎯 Độ tin cậy: {prediction.Confidence * 100:0.00}% - Nguồn: {prediction.Message}");
        _logger.LogInformation("------------------------------------------------");
        
        // TODO: Bước tiếp theo - Lưu vào PostgreSQL (PostGIS)
    }
}
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
        }
    }
}