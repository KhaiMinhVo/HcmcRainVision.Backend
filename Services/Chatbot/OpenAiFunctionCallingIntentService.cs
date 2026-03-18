using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public class OpenAiFunctionCallingIntentService : ILlmIntentService
    {
        private readonly HttpClient _httpClient;
        private readonly ChatbotLlmOptions _options;
        private readonly ILogger<OpenAiFunctionCallingIntentService> _logger;

        public OpenAiFunctionCallingIntentService(
            HttpClient httpClient,
            IOptions<ChatbotLlmOptions> options,
            ILogger<OpenAiFunctionCallingIntentService> logger)
        {
            _httpClient = httpClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task<LlmIntentResult?> ParseIntentAsync(string userMessage, CancellationToken cancellationToken = default)
        {
            if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.ApiKey))
            {
                return null;
            }

            try
            {
                var requestBody = BuildRequestBody(userMessage);
                using var request = new HttpRequestMessage(HttpMethod.Post, BuildChatCompletionEndpoint())
                {
                    Content = new StringContent(requestBody, Encoding.UTF8, "application/json")
                };

                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("LLM intent parser failed with status code {StatusCode}", response.StatusCode);
                    return null;
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                return ParseToolCall(json);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "LLM intent parsing failed. Falling back to rule-based parser.");
                return null;
            }
        }

        private string BuildChatCompletionEndpoint()
        {
            var baseUrl = _options.BaseUrl.TrimEnd('/');
            return $"{baseUrl}/chat/completions";
        }

        private string BuildRequestBody(string userMessage)
        {
            var normalizedMessage = VietnameseTextNormalizer.NormalizeForIntent(userMessage);

            var payload = new
            {
                model = _options.Model,
                temperature = 0,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = "Ban la bo phan nhan dien y dinh cho tro ly mua tai TP.HCM sau khi sap nhap Binh Duong va Ba Ria Vung Tau (QD2913/2025). Co 168 phuong/xa chia thanh 16 cum thi dua. Nguoi dung co the hoi theo: ten phuong (Ben Thanh, Di An, Thu Dau Mot...), cum so (cum 1 den 16), khu vuc moi (Khu trung tam, Khu Nam Sai Gon, Khu Cho Lon, TP. Thu Duc, Binh Duong, Ba Ria Vung Tau...), hoac quan cu (Quan 1, Tan Binh, Binh Thanh...). Ho tro tieng Viet co dau/khong dau, viet tat q1, tphcm, brvt, di tu...den. Luon chon function de tra intent co cau truc. Neu la district_rain, tra ten nguyen ban nguoi dung goi (vi du: 'cum 3', 'Thu Duc', 'Binh Duong', 'Quan 1', 'Ben Thanh', 'Khu Cho Lon')."
                    },
                    new
                    {
                        role = "user",
                        content = $"original: {userMessage}\nnormalized: {normalizedMessage}"
                    }
                },
                tools = new object[]
                {
                    new
                    {
                        type = "function",
                        function = new
                        {
                            name = "resolve_rain_query",
                            description = "Xac dinh nguoi dung dang hoi mua theo quan hay theo lo trinh",
                            parameters = new
                            {
                                type = "object",
                                properties = new
                                {
                                    intent = new
                                    {
                                        type = "string",
                                        @enum = new[] { "district_rain", "route_rain", "unknown" }
                                    },
                                    district = new { type = "string" },
                                    origin = new { type = "string" },
                                    destination = new { type = "string" },
                                    confidence = new { type = "number" }
                                },
                                required = new[] { "intent" },
                                additionalProperties = false
                            }
                        }
                    }
                },
                tool_choice = "auto"
            };

            return JsonSerializer.Serialize(payload);
        }

        private LlmIntentResult? ParseToolCall(JsonDocument json)
        {
            if (!json.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            {
                return null;
            }

            var message = choices[0].GetProperty("message");
            if (!message.TryGetProperty("tool_calls", out var toolCalls) || toolCalls.GetArrayLength() == 0)
            {
                return null;
            }

            var function = toolCalls[0].GetProperty("function");
            if (!function.TryGetProperty("arguments", out var argumentsElement))
            {
                return null;
            }

            var argsJson = argumentsElement.GetString();
            if (string.IsNullOrWhiteSpace(argsJson))
            {
                return null;
            }

            using var argsDoc = JsonDocument.Parse(argsJson);
            var root = argsDoc.RootElement;

            var intent = root.TryGetProperty("intent", out var intentEl)
                ? intentEl.GetString() ?? "unknown"
                : "unknown";

            var district = root.TryGetProperty("district", out var districtEl) ? districtEl.GetString() : null;
            var origin = root.TryGetProperty("origin", out var originEl) ? originEl.GetString() : null;
            var destination = root.TryGetProperty("destination", out var destinationEl) ? destinationEl.GetString() : null;

            district = string.IsNullOrWhiteSpace(district) ? null : VietnameseTextNormalizer.CanonicalizeDistrict(district);
            origin = string.IsNullOrWhiteSpace(origin) ? null : VietnameseTextNormalizer.CanonicalizeLocation(origin);
            destination = string.IsNullOrWhiteSpace(destination) ? null : VietnameseTextNormalizer.CanonicalizeLocation(destination);

            var confidence = 0d;
            if (root.TryGetProperty("confidence", out var confidenceEl) && confidenceEl.ValueKind == JsonValueKind.Number)
            {
                confidence = confidenceEl.GetDouble();
            }

            return new LlmIntentResult
            {
                Intent = intent,
                District = district,
                Origin = origin,
                Destination = destination,
                Confidence = confidence,
                RawArguments = argsJson
            };
        }
    }
}
