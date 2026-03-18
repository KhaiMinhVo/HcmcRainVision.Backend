using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public class RainAssistantService : IRainAssistantService
    {
        private readonly AppDbContext _dbContext;

        private const double RainAlertRadiusDegrees = 0.009;

        public RainAssistantService(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<DistrictRainResult?> GetDistrictRainAsync(string districtName, CancellationToken cancellationToken = default)
        {
            var normalizedInput = VietnameseTextNormalizer.NormalizeForIntent(districtName);
            var timeLimit = DateTime.UtcNow.AddMinutes(-30);

            // --- 1. Thử khớp theo ClusterNumber (ví dụ: "cụm 3", "cum thi dua 3") ---
            var clusterMatch = System.Text.RegularExpressions.Regex.Match(normalizedInput, @"\bcum\s*(?:thi\s*dua\s*)?(\d{1,2})\b");
            if (clusterMatch.Success && int.TryParse(clusterMatch.Groups[1].Value, out var clusterNum))
            {
                return await GetRainByClusterAsync(clusterNum, timeLimit, cancellationToken);
            }

            // --- 2. Thử khớp theo WardName (tên phường/xã chính xác theo QĐ2913) ---
            var allWards = await _dbContext.Wards
                .Select(w => new { w.WardId, w.WardName, w.DistrictName, w.ClusterNumber })
                .ToListAsync(cancellationToken);

            var matchedWard = allWards.FirstOrDefault(w =>
                VietnameseTextNormalizer.NormalizeForIntent(w.WardName) == normalizedInput);
            matchedWard ??= allWards.FirstOrDefault(w =>
                VietnameseTextNormalizer.NormalizeForIntent(w.WardName).Contains(normalizedInput) ||
                normalizedInput.Contains(VietnameseTextNormalizer.NormalizeForIntent(w.WardName)));

            if (matchedWard != null)
            {
                var wardCameraIds = await _dbContext.Cameras
                    .Where(c => c.WardId == matchedWard.WardId)
                    .Select(c => c.Id)
                    .ToListAsync(cancellationToken);

                if (wardCameraIds.Count > 0)
                {
                    return await BuildDistrictRainResultAsync(
                        matchedWard.WardName, wardCameraIds, timeLimit, cancellationToken);
                }
            }

            // --- 3. Thử khớp theo DistrictName (khu vực địa lý tổng hợp) ---
            var availableDistricts = allWards
                .Where(w => w.DistrictName != null)
                .Select(w => w.DistrictName!)
                .Distinct()
                .ToList();

            var resolvedDistrict = availableDistricts
                .FirstOrDefault(d => VietnameseTextNormalizer.NormalizeForIntent(d) == normalizedInput)
                ?? availableDistricts.FirstOrDefault(d => VietnameseTextNormalizer.NormalizeForIntent(d).Contains(normalizedInput)
                    || normalizedInput.Contains(VietnameseTextNormalizer.NormalizeForIntent(d)));

            if (string.IsNullOrWhiteSpace(resolvedDistrict))
            {
                return null;
            }

            var camerasInDistrict = await _dbContext.Cameras
                .Where(c => c.Ward != null && c.Ward.DistrictName == resolvedDistrict)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            if (camerasInDistrict.Count == 0)
            {
                return null;
            }

            return await BuildDistrictRainResultAsync(resolvedDistrict, camerasInDistrict, timeLimit, cancellationToken);
        }

        private async Task<DistrictRainResult?> GetRainByClusterAsync(int clusterNum, DateTime timeLimit, CancellationToken cancellationToken)
        {
            var cameraIds = await _dbContext.Cameras
                .Where(c => c.Ward != null && c.Ward.ClusterNumber == clusterNum)
                .Select(c => c.Id)
                .ToListAsync(cancellationToken);

            if (cameraIds.Count == 0)
            {
                return null;
            }

            return await BuildDistrictRainResultAsync($"Cụm {clusterNum}", cameraIds, timeLimit, cancellationToken);
        }

        private async Task<DistrictRainResult> BuildDistrictRainResultAsync(
            string areaLabel, List<string> cameraIds, DateTime timeLimit, CancellationToken cancellationToken)
        {
            var latestLogsByCamera = await _dbContext.WeatherLogs
                .Where(log => log.CameraId != null && cameraIds.Contains(log.CameraId) && log.Timestamp >= timeLimit)
                .GroupBy(log => log.CameraId)
                .Select(group => group.OrderByDescending(log => log.Timestamp).First())
                .ToListAsync(cancellationToken);

            var total = latestLogsByCamera.Count;
            if (total == 0)
            {
                return new DistrictRainResult
                {
                    DistrictName = areaLabel,
                    TotalCameras = cameraIds.Count,
                    RainingCameras = 0,
                    RainRatio = 0,
                    Level = "no_recent_data",
                    ObservedAtUtc = DateTime.UtcNow
                };
            }

            var raining = latestLogsByCamera.Count(log => log.IsRaining);
            var ratio = (double)raining / total;

            return new DistrictRainResult
            {
                DistrictName = areaLabel,
                TotalCameras = total,
                RainingCameras = raining,
                RainRatio = Math.Round(ratio, 3),
                Level = GetRainLevel(ratio),
                ObservedAtUtc = DateTime.UtcNow
            };
        }

        public async Task<RouteRainResult> AnalyzeRouteRainAsync(List<RoutePointDto> routePoints, CancellationToken cancellationToken = default)
        {
            if (routePoints.Count < 2)
            {
                return new RouteRainResult
                {
                    IsSafe = true,
                    RiskScore = 0,
                    RainyPointRatio = 0,
                    AverageRainIntensity = 0,
                    PeakRainIntensity = 0,
                    SamplePointCount = routePoints.Count,
                    RainyPointCount = 0
                };
            }

            var timeLimit = DateTime.UtcNow.AddMinutes(-30);
            var rainingLogs = await _dbContext.WeatherLogs
                .Where(x => x.IsRaining && x.Timestamp >= timeLimit && x.Location != null)
                .Select(x => new
                {
                    x.CameraId,
                    x.Confidence,
                    Location = x.Location!
                })
                .ToListAsync(cancellationToken);

            if (rainingLogs.Count == 0)
            {
                return new RouteRainResult
                {
                    IsSafe = true,
                    RiskScore = 0,
                    RainyPointRatio = 0,
                    AverageRainIntensity = 0,
                    PeakRainIntensity = 0,
                    SamplePointCount = routePoints.Count,
                    RainyPointCount = 0
                };
            }

            var sampled = SampleRoute(routePoints, 60);
            var rainyPointCount = 0;
            var intensityValues = new List<double>();
            var warnings = new List<object>();

            foreach (var point in sampled)
            {
                var pointGeom = new Point(point.Lng, point.Lat) { SRID = 4326 };

                var nearbyLogs = rainingLogs
                    .Where(log => log.Location.Distance(pointGeom) <= RainAlertRadiusDegrees)
                    .OrderByDescending(log => log.Confidence)
                    .ToList();

                if (nearbyLogs.Count == 0)
                {
                    continue;
                }

                rainyPointCount++;
                var maxConfidence = nearbyLogs.Max(log => (double)log.Confidence);
                intensityValues.Add(maxConfidence);

                var strongest = nearbyLogs.First();
                warnings.Add(new
                {
                    Lat = strongest.Location.Y,
                    Lng = strongest.Location.X,
                    CameraId = strongest.CameraId,
                    Confidence = Math.Round(strongest.Confidence, 3),
                    Message = $"Rain near camera {strongest.CameraId}"
                });
            }

            var sampleCount = sampled.Count;
            var ratio = sampleCount == 0 ? 0 : (double)rainyPointCount / sampleCount;
            var avg = intensityValues.Count == 0 ? 0 : intensityValues.Average();
            var peak = intensityValues.Count == 0 ? 0 : intensityValues.Max();

            var risk = 100 * ((0.5 * avg) + (0.3 * ratio) + (0.2 * peak));
            risk = Math.Round(Math.Clamp(risk, 0, 100), 1);

            return new RouteRainResult
            {
                IsSafe = risk <= 20,
                RiskScore = risk,
                RainyPointRatio = Math.Round(ratio, 3),
                AverageRainIntensity = Math.Round(avg, 3),
                PeakRainIntensity = Math.Round(peak, 3),
                SamplePointCount = sampleCount,
                RainyPointCount = rainyPointCount,
                Warnings = warnings.Distinct().Take(10).Cast<object>().ToList()
            };
        }

        private static List<RoutePointDto> SampleRoute(List<RoutePointDto> routePoints, int maxPoints)
        {
            if (routePoints.Count <= maxPoints)
            {
                return routePoints;
            }

            var sampled = new List<RoutePointDto>();
            var step = (double)(routePoints.Count - 1) / (maxPoints - 1);

            for (var i = 0; i < maxPoints; i++)
            {
                var index = (int)Math.Round(i * step);
                sampled.Add(routePoints[Math.Min(index, routePoints.Count - 1)]);
            }

            return sampled;
        }

        private static string GetRainLevel(double rainRatio)
        {
            if (rainRatio == 0) return "none";
            if (rainRatio <= 0.25) return "light";
            if (rainRatio <= 0.6) return "moderate";
            return "heavy";
        }
    }
}
