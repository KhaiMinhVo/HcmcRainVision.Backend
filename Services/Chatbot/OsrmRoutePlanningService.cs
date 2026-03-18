using System.Text.Json;
using HcmcRainVision.Backend.Models.DTOs;
using System.Globalization;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public class OsrmRoutePlanningService : IRoutePlanningService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<OsrmRoutePlanningService> _logger;

        public OsrmRoutePlanningService(HttpClient httpClient, ILogger<OsrmRoutePlanningService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<List<RoutePointDto>> BuildRouteAsync(string origin, string destination, CancellationToken cancellationToken = default)
        {
            var originPoint = await GeocodeAsync(origin, cancellationToken);
            var destinationPoint = await GeocodeAsync(destination, cancellationToken);

            var routeUrl = $"https://router.project-osrm.org/route/v1/driving/{originPoint.Lng},{originPoint.Lat};{destinationPoint.Lng},{destinationPoint.Lat}?overview=full&geometries=geojson";
            using var response = await _httpClient.GetAsync(routeUrl, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (!json.RootElement.TryGetProperty("routes", out var routes) || routes.GetArrayLength() == 0)
            {
                throw new InvalidOperationException("OSRM did not return any route.");
            }

            var coordinates = routes[0].GetProperty("geometry").GetProperty("coordinates");
            var points = new List<RoutePointDto>(coordinates.GetArrayLength());

            foreach (var coordinate in coordinates.EnumerateArray())
            {
                points.Add(new RoutePointDto
                {
                    Lng = coordinate[0].GetDouble(),
                    Lat = coordinate[1].GetDouble()
                });
            }

            return points;
        }

        private async Task<RoutePointDto> GeocodeAsync(string place, CancellationToken cancellationToken)
        {
            // Cho phép truyền trực tiếp "lat,lng" để dùng vị trí hiện tại từ client/server.
            var raw = place?.Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lng)
                    && lat is >= -90 and <= 90
                    && lng is >= -180 and <= 180)
                {
                    return new RoutePointDto { Lat = lat, Lng = lng };
                }
            }

            var query = Uri.EscapeDataString($"{place}, Ho Chi Minh City, Vietnam");
            var url = $"https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q={query}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("HcmcRainVisionBot/1.0");

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0)
            {
                throw new InvalidOperationException($"Cannot geocode place: {place}");
            }

            var first = json.RootElement[0];
            var latText = first.GetProperty("lat").GetString();
            var lngText = first.GetProperty("lon").GetString();

            if (!double.TryParse(latText, out var geocodedLat) || !double.TryParse(lngText, out var geocodedLng))
            {
                throw new InvalidOperationException($"Invalid geocode response for place: {place}");
            }

            return new RoutePointDto { Lat = geocodedLat, Lng = geocodedLng };
        }
    }
}
