using System.Text;
using System.Text.Json;
using HcmcRainVision.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public interface IChatbotService
    {
        Task<string> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default);
    }

    public class ChatbotService : IChatbotService
    {
        private readonly AppDbContext _db;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _apiKey;
        private readonly ILogger<ChatbotService> _logger;

        private const string GeminiEndpoint =
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent";

        public ChatbotService(
            AppDbContext db,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<ChatbotService> logger)
        {
            _db = db;
            _httpClientFactory = httpClientFactory;
            _apiKey = configuration["Gemini:ApiKey"] ?? string.Empty;
            _logger = logger;
        }

        public async Task<string> GetResponseAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            var rainContext = await BuildRainContextAsync(cancellationToken);
            return await CallGeminiAsync(userMessage, rainContext, cancellationToken);
        }

        // Query DB: rain status by district + ward (last 30 min)
        private async Task<string> BuildRainContextAsync(CancellationToken cancellationToken)
        {
            try
            {
                var timeLimit = DateTime.UtcNow.AddMinutes(-30);

                // Load cameras with ward info
                var cameras = await _db.Cameras
                    .Include(c => c.Ward)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                // Load recent weather logs
                var logs = await _db.WeatherLogs
                    .Where(l => l.Timestamp >= timeLimit)
                    .AsNoTracking()
                    .ToListAsync(cancellationToken);

                if (!logs.Any())
                    return "Hiện tại chưa có dữ liệu mưa mới (dữ liệu được cập nhật mỗi 5 phút).";

                // Build lookup: cameraId → district + ward
                var cameraMap = cameras.ToDictionary(c => c.Id, c => new
                {
                    District = c.Ward?.DistrictName ?? "Không xác định",
                    Ward = c.Ward?.WardName ?? "Không xác định"
                });

                // Group logs by district
                var grouped = logs
                    .Where(l => l.CameraId != null && cameraMap.ContainsKey(l.CameraId))
                    .GroupBy(l => cameraMap[l.CameraId!].District)
                    .Select(g =>
                    {
                        var total = g.Count();
                        var raining = g.Count(l => l.IsRaining);
                        return new
                        {
                            District = g.Key,
                            Total = total,
                            Raining = raining,
                            IsRaining = raining > 0,
                            RainRatio = total > 0 ? (double)raining / total : 0
                        };
                    })
                    .OrderBy(d => d.District)
                    .ToList();

                // Also build ward-level detail for raining districts
                var wardDetails = logs
                    .Where(l => l.CameraId != null && cameraMap.ContainsKey(l.CameraId!) && l.IsRaining)
                    .GroupBy(l => new
                    {
                        District = cameraMap[l.CameraId!].District,
                        Ward = cameraMap[l.CameraId!].Ward
                    })
                    .Select(g => $"{g.Key.Ward} ({g.Key.District})")
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();

                var lines = new List<string>
                {
                    $"Thời điểm cập nhật: {DateTime.UtcNow.AddHours(7):HH:mm} (giờ VN)",
                    "=== Tình trạng mưa theo quận ==="
                };

                foreach (var d in grouped)
                {
                    var status = d.IsRaining
                        ? $"CÓ MƯA ({d.Raining}/{d.Total} điểm quan sát)"
                        : $"Không mưa ({d.Total} điểm quan sát)";
                    lines.Add($"- {d.District}: {status}");
                }

                if (wardDetails.Any())
                {
                    lines.Add("=== Phường/xã đang có mưa ===");
                    foreach (var w in wardDetails)
                        lines.Add($"- {w}");
                }

                return string.Join("\n", lines);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi lấy dữ liệu mưa cho chatbot");
                return "Không thể lấy dữ liệu mưa tại thời điểm này.";
            }
        }

        private async Task<string> CallGeminiAsync(string userMessage, string rainContext, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_apiKey))
                return "Chatbot chưa được cấu hình API key. Vui lòng liên hệ quản trị viên.";

            var systemPrompt = $"""
                Bạn là trợ lý thời tiết của hệ thống HCMCRainVision - hệ thống giám sát mưa TP.HCM bằng camera AI.
                
                DỮ LIỆU THỰC TẾ TỪ HỆ THỐNG (cập nhật mỗi 5 phút):
                {rainContext}
                
                QUY TẮC:
                - Chỉ trả lời các câu hỏi liên quan đến thời tiết, mưa tại TP.HCM.
                - Chỉ sử dụng dữ liệu được cung cấp ở trên, không đoán mò hoặc dùng kiến thức ngoài.
                - Nếu không có dữ liệu về khu vực được hỏi, nói rõ "không có dữ liệu cho khu vực này".
                - Trả lời bằng tiếng Việt, ngắn gọn (tối đa 3-4 câu).
                - Không trả lời câu hỏi ngoài phạm vi thời tiết TP.HCM.
                - Khi được hỏi về tuyến đường, hãy đề cập các quận/phường nằm trên tuyến đó dựa vào dữ liệu có.
                """;

            var requestBody = new
            {
                system_instruction = new
                {
                    parts = new[] { new { text = systemPrompt } }
                },
                contents = new[]
                {
                    new
                    {
                        role = "user",
                        parts = new[] { new { text = userMessage } }
                    }
                },
                generationConfig = new
                {
                    maxOutputTokens = 400,
                    temperature = 0.2
                }
            };

            var json = JsonSerializer.Serialize(requestBody);
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);

            try
            {
                var response = await client.PostAsync(
                    $"{GeminiEndpoint}?key={_apiKey}",
                    new StringContent(json, Encoding.UTF8, "application/json"),
                    cancellationToken);

                var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Gemini API lỗi {Status}: {Body}", response.StatusCode, responseBody);
                    return "Không thể kết nối với trợ lý AI lúc này. Vui lòng thử lại sau.";
                }

                using var doc = JsonDocument.Parse(responseBody);
                var text = doc.RootElement
                    .GetProperty("candidates")[0]
                    .GetProperty("content")
                    .GetProperty("parts")[0]
                    .GetProperty("text")
                    .GetString();

                return text ?? "Không có phản hồi từ trợ lý AI.";
            }
            catch (TaskCanceledException)
            {
                return "Trợ lý AI phản hồi quá chậm. Vui lòng thử lại.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Lỗi khi gọi Gemini API");
                return "Đã xảy ra lỗi khi xử lý câu hỏi. Vui lòng thử lại.";
            }
        }
    }
}
