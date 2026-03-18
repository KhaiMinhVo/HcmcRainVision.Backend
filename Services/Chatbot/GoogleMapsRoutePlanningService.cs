using System.Globalization;
using System.Text.Json;
using HcmcRainVision.Backend.Models.DTOs;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    /// <summary>
    /// Route planning service backed by Google Maps Geocoding API + Directions API.
    /// Set GoogleMaps:ApiKey in configuration to enable.
    /// </summary>
    public class GoogleMapsRoutePlanningService : IRoutePlanningService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GoogleMapsRoutePlanningService> _logger;
        private readonly string _apiKey;

        private const string GeocodingBaseUrl = "https://maps.googleapis.com/maps/api/geocode/json";
        private const string DirectionsBaseUrl = "https://maps.googleapis.com/maps/api/directions/json";

        public GoogleMapsRoutePlanningService(
            HttpClient httpClient,
            ILogger<GoogleMapsRoutePlanningService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["GoogleMaps:ApiKey"] ?? string.Empty;
        }

        public async Task<List<RoutePointDto>> BuildRouteAsync(
            string origin, string destination, CancellationToken cancellationToken = default)
        {
            var originPoint  = await GeocodeAsync(origin, cancellationToken);
            var destPoint    = await GeocodeAsync(destination, cancellationToken);

            // Google Directions API: origin/destination as "lat,lng"
            var originStr = FormattableString.Invariant($"{originPoint.Lat},{originPoint.Lng}");
            var destStr   = FormattableString.Invariant($"{destPoint.Lat},{destPoint.Lng}");

            var url = $"{DirectionsBaseUrl}" +
                      $"?origin={Uri.EscapeDataString(originStr)}" +
                      $"&destination={Uri.EscapeDataString(destStr)}" +
                      $"&mode=driving" +
                      $"&overview=full" +
                      $"&key={_apiKey}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var status = json.RootElement.GetProperty("status").GetString();
            if (status != "OK")
            {
                var errorMsg = json.RootElement.TryGetProperty("error_message", out var em)
                    ? em.GetString()
                    : status;
                _logger.LogError("Google Directions API returned status {Status}: {Error}", status, errorMsg);
                throw new InvalidOperationException($"Google Directions API error: {errorMsg}");
            }

            var routes = json.RootElement.GetProperty("routes");
            if (routes.GetArrayLength() == 0)
                throw new InvalidOperationException("Google Directions API returned no routes.");

            var polylineEncoded = routes[0]
                .GetProperty("overview_polyline")
                .GetProperty("points")
                .GetString()!;

            return DecodePolyline(polylineEncoded);
        }

        // ─── Private helpers ────────────────────────────────────────────────────

        private async Task<RoutePointDto> GeocodeAsync(string place, CancellationToken cancellationToken)
        {
            // Accept "lat,lng" directly — skip API call
            var raw = place?.Trim();
            if (!string.IsNullOrWhiteSpace(raw))
            {
                var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2
                    && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var latParsed)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lngParsed)
                    && latParsed is >= -90 and <= 90
                    && lngParsed is >= -180 and <= 180)
                {
                    return new RoutePointDto { Lat = latParsed, Lng = lngParsed };
                }
            }

            // Google Geocoding — bias results to Vietnam (region=vn)
            var address = $"{place}, Ho Chi Minh City, Vietnam";
            var url = $"{GeocodingBaseUrl}" +
                      $"?address={Uri.EscapeDataString(address)}" +
                      $"&region=vn" +
                      $"&language=vi" +
                      $"&key={_apiKey}";

            using var response = await _httpClient.GetAsync(url, cancellationToken);
            response.EnsureSuccessStatusCode();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

            var status = json.RootElement.GetProperty("status").GetString();
            if (status != "OK")
            {
                var errorMsg = json.RootElement.TryGetProperty("error_message", out var em)
                    ? em.GetString()
                    : status;
                _logger.LogError("Google Geocoding API returned status {Status} for place '{Place}': {Error}",
                    status, place, errorMsg);
                throw new InvalidOperationException($"Cannot geocode '{place}': {errorMsg}");
            }

            var location = json.RootElement
                .GetProperty("results")[0]
                .GetProperty("geometry")
                .GetProperty("location");

            return new RoutePointDto
            {
                Lat = location.GetProperty("lat").GetDouble(),
                Lng = location.GetProperty("lng").GetDouble()
            };
        }

        /// <summary>
        /// Decodes a Google Maps encoded polyline string into a list of lat/lng points.
        /// https://developers.google.com/maps/documentation/utilities/polylinealgorithm
        /// </summary>
        private static List<RoutePointDto> DecodePolyline(string encoded)
        {
            var points = new List<RoutePointDto>();
            int index = 0, lat = 0, lng = 0;

            while (index < encoded.Length)
            {
                lat += DecodeChunk(encoded, ref index);
                lng += DecodeChunk(encoded, ref index);
                points.Add(new RoutePointDto
                {
                    Lat = lat / 1e5,
                    Lng = lng / 1e5
                });
            }

            return points;
        }

        private static int DecodeChunk(string encoded, ref int index)
        {
            int result = 0, shift = 0, b;
            do
            {
                b = encoded[index++] - 63;
                result |= (b & 0x1F) << shift;
                shift += 5;
            } while (b >= 0x20);

            return (result & 1) != 0 ? ~(result >> 1) : result >> 1;
        }
    }
}
