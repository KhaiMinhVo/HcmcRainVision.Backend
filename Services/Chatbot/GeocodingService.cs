using System.Globalization;
using System.Text.Json;
using HcmcRainVision.Backend.Models.DTOs;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    /// <summary>
    /// Geocoding Service sử dụng Nominatim (OpenStreetMap) API
    /// Miễn phí, không cần API key, phù hợp cho TP.HCM
    /// </summary>
    public class GeocodingService : IGeocodingService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GeocodingService> _logger;

        public GeocodingService(HttpClient httpClient, ILogger<GeocodingService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<RoutePointDto> GeocodeAsync(string place, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(place))
                throw new ArgumentException("Địa chỉ không được để trống");

            var raw = place.Trim();

            // Nếu user nhập "lat,lng" trực tiếp, dùng luôn
            if (TryParseLatLng(raw, out var lat, out var lng))
            {
                _logger.LogInformation("Parsed direct coordinates from input: {Lat},{Lng}", lat, lng);
                return new RoutePointDto { Lat = lat, Lng = lng };
            }

            // Nếu là tên quận/phường quen thuộc, dùng bản đồ sẵn
            if (TryGetKnownDistrict(raw, out var knownLat, out var knownLng))
            {
                _logger.LogInformation("Using known district coordinates for '{Place}': {Lat},{Lng}", 
                    place, knownLat, knownLng);
                return new RoutePointDto { Lat = knownLat, Lng = knownLng };
            }

            // Nếu không, gọi Nominatim API
            return await GeocodeViaNominatimAsync(raw, cancellationToken);
        }

        /// <summary>
        /// Kiểm tra xem input có phải "lat,lng" không
        /// </summary>
        private static bool TryParseLatLng(string input, out double lat, out double lng)
        {
            lat = 0;
            lng = 0;

            var parts = input.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 2) return false;

            if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out lat))
                return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out lng))
                return false;

            return lat is >= -90 and <= 90 && lng is >= -180 and <= 180;
        }

        /// <summary>
        /// Tra cứu các quận/phường nổi tiếng của TP.HCM
        /// </summary>
        private static bool TryGetKnownDistrict(string place, out double lat, out double lng)
        {
            lat = 0;
            lng = 0;

            // Bản đồ các quận và trung tâm của chúng
            var knownDistricts = new Dictionary<string, (double lat, double lng)>(StringComparer.OrdinalIgnoreCase)
            {
                // Quận/huyện chính
                { "Quận 1", (10.7769, 106.6873) },
                { "Quận 2", (10.8019, 106.7685) },
                { "Quận 3", (10.7873, 106.6941) },
                { "Quận 4", (10.7567, 106.7033) },
                { "Quận 5", (10.7639, 106.6685) },
                { "Quận 6", (10.7466, 106.6600) },
                { "Quận 7", (10.7321, 106.7169) },
                { "Quận 8", (10.7344, 106.6925) },
                { "Quận 9", (10.8450, 106.7900) },
                { "Quận 10", (10.7659, 106.6635) },
                { "Quận 11", (10.8163, 106.6576) },
                { "Quận 12", (10.8714, 106.6845) },
                { "Quận Bình Tân", (10.7980, 106.6291) },
                { "Quận Bình Thạnh", (10.8237, 106.7381) },
                { "Quận Gò Vấp", (10.8468, 106.6932) },
                { "Quận Phú Nhuận", (10.8069, 106.6947) },
                { "Quận Tân Bình", (10.8051, 106.6547) },
                { "Quận Tân Phú", (10.7918, 106.6359) },

                // Thành phố Thủ Đức (TP lớn)
                { "Thủ Đức", (10.8217, 106.7760) },
                { "TP Thủ Đức", (10.8217, 106.7760) },

                // Huyện
                { "Huyện Bình Chánh", (10.6618, 106.5630) },
                { "Huyện Cần Giờ", (10.3525, 106.9931) },
                { "Huyện Hóc Môn", (10.8705, 106.5730) },
                { "Huyện Nhà Bè", (10.4789, 106.7442) },
                { "Huyện Củ Chi", (10.9659, 106.4228) },

                // Các tên thay thế
                { "Q1", (10.7769, 106.6873) },
                { "Q2", (10.8019, 106.7685) },
                { "Q3", (10.7873, 106.6941) },
                { "Thuduc", (10.8217, 106.7760) },
                { "Thu Duc", (10.8217, 106.7760) },
                { "Ben Thanh", (10.7734, 106.6947) },
                { "Bến Thành", (10.7734, 106.6947) },
                { "Cyclo", (10.7738, 106.6918) }, // Gần Bến Thành
                { "Dam Sen", (10.7516, 106.6808) },
                { "Đầm Sen", (10.7516, 106.6808) },
                { "Tan Son Nhat", (10.8152, 106.6619) },
                { "Tân Sơn Nhất", (10.8152, 106.6619) },
                { "District 1", (10.7769, 106.6873) },
                { "District 2", (10.8019, 106.7685) },
            };

            if (knownDistricts.TryGetValue(place, out var coords))
            {
                lat = coords.lat;
                lng = coords.lng;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gọi Nominatim API để geocode địa chỉ
        /// </summary>
        private async Task<RoutePointDto> GeocodeViaNominatimAsync(string place, CancellationToken cancellationToken)
        {
            try
            {
                var query = Uri.EscapeDataString($"{place}, Ho Chi Minh City, Vietnam");
                var url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q={query}";

                _logger.LogInformation("Geocoding '{Place}' via Nominatim: {Url}", place, url);

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.UserAgent.ParseAdd("HcmcRainVisionBot/1.0");

                using var response = await _httpClient.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

                if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
                {
                    _logger.LogWarning("Nominatim returned no results for '{Place}'", place);
                    throw new InvalidOperationException($"Không tìm thấy '{place}' trên bản đồ");
                }

                var first = json.RootElement[0];
                var latText = first.GetProperty("lat").GetString();
                var lngText = first.GetProperty("lon").GetString();

                if (!double.TryParse(latText, out var lat) || !double.TryParse(lngText, out var lng))
                {
                    _logger.LogWarning("Failed to parse coordinates for '{Place}': lat={Lat}, lng={Lng}", 
                        place, latText, lngText);
                    throw new InvalidOperationException($"Không thể xác định tọa độ cho '{place}'");
                }

                _logger.LogInformation("Successfully geocoded '{Place}': {Lat},{Lng}", place, lat, lng);
                return new RoutePointDto { Lat = lat, Lng = lng };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error while geocoding '{Place}'", place);
                throw new InvalidOperationException($"Lỗi kết nối khi tìm '{place}'", ex);
            }
            catch (Exception ex) when (ex is not InvalidOperationException)
            {
                _logger.LogError(ex, "Unexpected error while geocoding '{Place}'", place);
                throw new InvalidOperationException($"Lỗi khi xác định vị trí '{place}'", ex);
            }
        }
    }
}
