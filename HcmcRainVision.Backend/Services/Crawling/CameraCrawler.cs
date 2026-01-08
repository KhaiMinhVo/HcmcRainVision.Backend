using System.Net.Http.Headers;

namespace HcmcRainVision.Backend.Services.Crawling
{
    public interface ICameraCrawler
    {
        Task<byte[]?> FetchImageAsync(string url);
    }

    public class CameraCrawler : ICameraCrawler
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<CameraCrawler> _logger;
        
        // Danh sách User-Agent để thay đổi liên tục, tránh bị firewall phát hiện là bot
        private readonly string[] _userAgents = new[]
        {
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
            "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.0.0 Safari/537.36"
        };

        public CameraCrawler(IHttpClientFactory httpClientFactory, ILogger<CameraCrawler> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<byte[]?> FetchImageAsync(string url)
        {
            // 1. Chế độ giả lập (Dành cho lúc test hoặc API thật bị sập)
            if (url.StartsWith("TEST_MODE"))
            {
                return await GetFakeImageAsync(); 
            }

            // 2. Tạo Client từ Factory
            var client = _httpClientFactory.CreateClient();
            
            // 3. Cấu hình Headers để "lừa" máy chủ camera
            var randomAgent = _userAgents[new Random().Next(_userAgents.Length)];
            client.DefaultRequestHeaders.UserAgent.ParseAdd(randomAgent);
            
            // Quan trọng: Nhiều camera HCMC yêu cầu Referer từ trang chủ của họ mới cho tải ảnh
            client.DefaultRequestHeaders.Referrer = new Uri("http://giaothong.hochiminhcity.gov.vn/"); 
            client.Timeout = TimeSpan.FromSeconds(10); // Đừng chờ quá 10s

            try 
            {
                _logger.LogInformation($"Đang tải ảnh từ: {url}");
                
                // Gửi request
                var response = await client.GetAsync(url);
                
                // Nếu lỗi (404, 403...) ném ra exception
                response.EnsureSuccessStatusCode();

                // Kiểm tra xem có đúng là ảnh không (Content-Type)
                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType != "image/jpeg" && mediaType != "image/png")
                {
                    _logger.LogWarning($"URL không trả về ảnh! Nhận được: {mediaType}");
                    return null; // Hoặc throw exception tùy bạn
                }

                // Đọc dữ liệu ảnh thành mảng byte (để chuyển cho AI xử lý)
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Lỗi khi crawl ảnh: {ex.Message}");
                return null; // Trả về null để Worker biết mà bỏ qua
            }
        }

        // Hàm giả lập ảnh (Load 1 ảnh có sẵn trong thư mục dự án)
        private async Task<byte[]> GetFakeImageAsync()
        {
            _logger.LogInformation("--- TEST MODE: Đang dùng ảnh giả lập ---");
            // Bạn hãy để 1 file ảnh tên 'sample_rain.jpg' vào thư mục gốc của dự án
            var path = Path.Combine(Directory.GetCurrentDirectory(), "sample_rain.jpg");
            
            if (File.Exists(path))
            {
                return await File.ReadAllBytesAsync(path);
            }
            return new byte[0]; // Trả về rỗng nếu không tìm thấy
        }
    }
}