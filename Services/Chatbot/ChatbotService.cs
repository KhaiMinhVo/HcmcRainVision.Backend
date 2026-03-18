using System.Text.RegularExpressions;
using System.Globalization;
using HcmcRainVision.Backend.Models.DTOs;
using HcmcRainVision.Backend.Data;
using Microsoft.EntityFrameworkCore;

namespace HcmcRainVision.Backend.Services.Chatbot
{
    public class ChatbotService : IChatbotService
    {
        private readonly IRainAssistantService _rainAssistantService;
        private readonly IRoutePlanningService _routePlanningService;
        private readonly ILlmIntentService _llmIntentService;
        private readonly ILogger<ChatbotService> _logger;
        private readonly AppDbContext _dbContext;

        public ChatbotService(
            IRainAssistantService rainAssistantService,
            IRoutePlanningService routePlanningService,
            ILlmIntentService llmIntentService,
            AppDbContext dbContext,
            ILogger<ChatbotService> logger)
        {
            _rainAssistantService = rainAssistantService;
            _routePlanningService = routePlanningService;
            _llmIntentService = llmIntentService;
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<ChatbotAskResponse> AskAsync(ChatbotAskRequest request, CancellationToken cancellationToken = default)
        {
            request.Origin = NormalizeLocationInput(request.Origin);
            request.Destination = NormalizeLocationInput(request.Destination);
            request.OriginLatitude = IsUsableCoordinate(request.OriginLatitude, request.OriginLongitude) ? request.OriginLatitude : null;
            request.OriginLongitude = IsUsableCoordinate(request.OriginLatitude, request.OriginLongitude) ? request.OriginLongitude : null;
            request.DestinationLatitude = IsUsableCoordinate(request.DestinationLatitude, request.DestinationLongitude) ? request.DestinationLatitude : null;
            request.DestinationLongitude = IsUsableCoordinate(request.DestinationLatitude, request.DestinationLongitude) ? request.DestinationLongitude : null;

            if (!IsUsableCoordinate(request.CurrentLatitude, request.CurrentLongitude))
            {
                request.CurrentLatitude = null;
                request.CurrentLongitude = null;
            }

            if (request.RoutePoints is { Count: > 0 })
            {
                request.RoutePoints = request.RoutePoints
                    .Where(p => IsUsableCoordinate(p.Lat, p.Lng))
                    .ToList();
            }

            var explicitOrigin = ResolveExplicitOrigin(request);
            var explicitDestination = ResolveExplicitDestination(request);

            var message = request.Message?.Trim() ?? string.Empty;
            var normalizedMessage = VietnameseTextNormalizer.NormalizeForIntent(message);
            var messageLooksRoute = IsLikelyRouteMessage(normalizedMessage);
            var hasExplicitRoutePayload = HasExplicitRoutePayload(request, explicitOrigin, explicitDestination);
            if (string.IsNullOrWhiteSpace(message))
            {
                return new ChatbotAskResponse
                {
                    Intent = "unknown",
                    Answer = "Ban hay nhap cau hoi, vi du: 'Quan 1 co mua khong?'"
                };
            }

            // Ưu tiên district intent theo nội dung câu hỏi để tránh FE gửi thừa destination làm lệch intent.
            var districtFromMessage = ExtractDistrict(normalizedMessage);
            if (!string.IsNullOrWhiteSpace(districtFromMessage)
                && !messageLooksRoute
                && !hasExplicitRoutePayload)
            {
                return await HandleDistrictIntentAsync(districtFromMessage!, cancellationToken);
            }

            // Mode 2: FE chọn cả origin + destination (bằng tên hoặc toạ độ)
            if ((messageLooksRoute || hasExplicitRoutePayload)
                && !string.IsNullOrWhiteSpace(explicitOrigin)
                && !string.IsNullOrWhiteSpace(explicitDestination))
            {
                return await HandleRouteIntentAsync(
                    explicitOrigin,
                    explicitDestination,
                    request,
                    cancellationToken,
                    modeUsed: "origin_destination_selected");
            }

            // Trường hợp route nhưng user chỉ đưa destination (không nói điểm đi):
            // ưu tiên destination từ request và tự lấy origin theo GPS/LastKnownLocation.
            if (messageLooksRoute
                && !IsMeaningfulLocation(request.Origin)
                && !string.IsNullOrWhiteSpace(explicitDestination))
            {
                var resolvedOrigin = await ResolveCurrentOriginAsync(request, cancellationToken);
                var requestDestination = explicitDestination;

                if (!string.IsNullOrWhiteSpace(resolvedOrigin))
                {
                    return await HandleRouteIntentAsync(
                        resolvedOrigin,
                        requestDestination,
                        request,
                        cancellationToken,
                        modeUsed: "gps_to_destination");
                }

                return new ChatbotAskResponse
                {
                    Intent = "route_rain",
                    Answer = "Ban chua cung cap diem xuat phat. Neu khong muon nhap origin, hay bat GPS thiet bi va gui currentLatitude/currentLongitude, hoac cap nhat vi tri qua /api/auth/update-location.",
                    Data = new
                    {
                        needOrigin = true,
                        needDestination = false,
                        destination = requestDestination,
                        hint = "auto_origin_from_gps_or_last_known_location"
                    }
                };
            }

            if (LooksLikeRouteButMissingEndpoints(normalizedMessage, request, out var hintedDestination))
            {
                var resolvedOrigin = await ResolveCurrentOriginAsync(request, cancellationToken);
                if (!string.IsNullOrWhiteSpace(resolvedOrigin) && !string.IsNullOrWhiteSpace(hintedDestination))
                {
                    return await HandleRouteIntentAsync(
                        resolvedOrigin,
                        hintedDestination!,
                        request,
                        cancellationToken);
                }

                return new ChatbotAskResponse
                {
                    Intent = "route_rain",
                    Answer = $"Ban dang hoi theo lo trinh, nhung thieu diem xuat phat. Hay nhap theo mau: 'Di tu A den B co mua khong?'. {(string.IsNullOrWhiteSpace(hintedDestination) ? string.Empty : $"Diem den hien tai toi nhan duoc: {hintedDestination}.")}".Trim(),
                    Data = new
                    {
                        needOrigin = true,
                        needDestination = false,
                        destination = hintedDestination
                    }
                };
            }

            var llmIntent = await _llmIntentService.ParseIntentAsync(message, cancellationToken);
            if (llmIntent != null)
            {
                if (llmIntent.Intent == "route_rain"
                    && (messageLooksRoute || hasExplicitRoutePayload)
                    && IsMeaningfulLocation(llmIntent.Origin)
                    && IsMeaningfulLocation(llmIntent.Destination))
                {
                    return await HandleRouteIntentAsync(
                        llmIntent.Origin!,
                        llmIntent.Destination!,
                        request,
                        cancellationToken);
                }

                if (llmIntent.Intent == "district_rain" && !string.IsNullOrWhiteSpace(llmIntent.District))
                {
                    return await HandleDistrictIntentAsync(llmIntent.District!, cancellationToken);
                }
            }

            if (TryExtractRoute(message, normalizedMessage, request, out var origin, out var destination))
            {
                return await HandleRouteIntentAsync(origin!, destination!, request, cancellationToken);
            }

            var district = districtFromMessage;
            if (!string.IsNullOrWhiteSpace(district))
            {
                return await HandleDistrictIntentAsync(district!, cancellationToken);
            }

            return new ChatbotAskResponse
            {
                Intent = "unknown",
                Answer = "Toi co the ho tro 2 kieu cau hoi: (1) Quan nao dang mua? (2) Di tu A den B co mua tren duong khong?"
            };
        }

        private async Task<ChatbotAskResponse> HandleDistrictIntentAsync(string district, CancellationToken cancellationToken)
        {
            // Chuẩn hóa tên khu vực: "Quận 1" → "Khu trung tâm", "Tân Bình" → "Khu Tân Bình - Phú Nhuận"...
            var canonicalDistrict = VietnameseTextNormalizer.CanonicalizeDistrict(district) ?? district;
            var result = await _rainAssistantService.GetDistrictRainAsync(canonicalDistrict, cancellationToken);
            if (result == null)
            {
                return new ChatbotAskResponse
                {
                    Intent = "district_rain",
                    Answer = $"Toi chua tim thay du lieu camera cho khu vuc '{district}'. Ban thu dung ten quan khac giup toi.",
                    Data = new { district }
                };
            }

            var percent = Math.Round(result.RainRatio * 100, 1);
            var answer = result.Level switch
            {
                "none" => $"Hien tai {result.DistrictName} khong ghi nhan mua trong 30 phut gan day.",
                "light" => $"{result.DistrictName} dang co mua nhe. Khoang {percent}% camera ghi nhan mua.",
                "moderate" => $"{result.DistrictName} dang mua muc vua. Khoang {percent}% camera ghi nhan mua.",
                "heavy" => $"{result.DistrictName} dang mua kha lon. Khoang {percent}% camera ghi nhan mua.",
                _ => $"{result.DistrictName} hien khong du du lieu moi trong 30 phut qua."
            };

            return new ChatbotAskResponse
            {
                Intent = "district_rain",
                Answer = answer,
                Data = result
            };
        }

        private async Task<ChatbotAskResponse> HandleRouteIntentAsync(
            string origin,
            string destination,
            ChatbotAskRequest request,
            CancellationToken cancellationToken,
            string? modeUsed = null)
        {
            try
            {
                List<RoutePointDto> routePoints;
                if (request.RoutePoints is { Count: >= 2 })
                {
                    routePoints = request.RoutePoints;
                }
                else
                {
                    routePoints = await _routePlanningService.BuildRouteAsync(origin, destination, cancellationToken);
                }

                var routeResult = await _rainAssistantService.AnalyzeRouteRainAsync(routePoints, cancellationToken);
                var hasExplicitOrigin = IsMeaningfulLocation(request.Origin)
                    || IsUsableCoordinate(request.OriginLatitude, request.OriginLongitude)
                    || request.RoutePoints is { Count: >= 2 };
                var hasExplicitDestination = IsMeaningfulLocation(request.Destination)
                    || IsUsableCoordinate(request.DestinationLatitude, request.DestinationLongitude)
                    || request.RoutePoints is { Count: >= 2 };
                var hasCurrentGpsOrigin = IsUsableCoordinate(request.CurrentLatitude, request.CurrentLongitude);
                routeResult.ModeUsed = modeUsed ?? ((hasExplicitOrigin && hasExplicitDestination) || request.RoutePoints is { Count: >= 2 }
                    ? "origin_destination_selected"
                    : "gps_to_destination");
                routeResult.HasExplicitOrigin = hasExplicitOrigin;
                routeResult.HasExplicitDestination = hasExplicitDestination;
                routeResult.HasCurrentGpsOrigin = hasCurrentGpsOrigin;

                var answer = BuildRouteAnswer(origin, destination, routeResult);

                return new ChatbotAskResponse
                {
                    Intent = "route_rain",
                    Answer = answer,
                    Data = routeResult
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Route analysis failed from {Origin} to {Destination}", origin, destination);

                return new ChatbotAskResponse
                {
                    Intent = "route_rain",
                    Answer = "Toi chua phan tich duoc lo trinh luc nay. Ban co the gui them routePoints (lat/lng) de toi kiem tra truc tiep.",
                    Data = new { origin, destination }
                };
            }
        }

        private static string BuildRouteAnswer(string origin, string destination, RouteRainResult result)
        {
            if (result.RiskScore <= 20)
            {
                return $"Lo trinh tu {origin} den {destination} kha an toan, chua thay dau hieu mua dang ke tren duong di.";
            }

            if (result.RiskScore <= 50)
            {
                return $"Lo trinh tu {origin} den {destination} co mua cuc bo. Risk score: {result.RiskScore}/100. Ban nen mang ao mua.";
            }

            if (result.RiskScore <= 80)
            {
                return $"Lo trinh tu {origin} den {destination} co nguy co mua cao. Risk score: {result.RiskScore}/100. Nen can nhac doi gio di.";
            }

            return $"Lo trinh tu {origin} den {destination} dang co nguy co mua rat cao. Risk score: {result.RiskScore}/100. Nen hoan chuyen neu co the.";
        }

        private static bool TryExtractRoute(string message, string normalizedMessage, ChatbotAskRequest request, out string? origin, out string? destination)
        {
            if (IsLikelyRouteRequest(normalizedMessage, request)
                && IsMeaningfulLocation(request.Origin)
                && IsMeaningfulLocation(request.Destination))
            {
                origin = VietnameseTextNormalizer.CanonicalizeLocation(request.Origin!.Trim());
                destination = VietnameseTextNormalizer.CanonicalizeLocation(request.Destination!.Trim());
                return true;
            }

            var fromToMatch = Regex.Match(
                normalizedMessage,
                @"tu\s+(?<origin>.+?)\s+den\s+(?<destination>.+)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!fromToMatch.Success)
            {
                origin = null;
                destination = null;
                return false;
            }

            origin = VietnameseTextNormalizer.CanonicalizeLocation(fromToMatch.Groups["origin"].Value.Trim());
            destination = VietnameseTextNormalizer.CanonicalizeLocation(fromToMatch.Groups["destination"].Value.Trim(' ', '?', '.', '!'));
            return !string.IsNullOrWhiteSpace(origin) && !string.IsNullOrWhiteSpace(destination);
        }

        private static bool IsLikelyRouteRequest(string normalizedMessage, ChatbotAskRequest request)
        {
            return IsLikelyRouteMessage(normalizedMessage)
                || request.RoutePoints is { Count: >= 2 }
                || (HasExplicitOriginSelection(request) && HasExplicitDestinationSelection(request));
        }

        private static bool IsLikelyRouteMessage(string normalizedMessage)
        {
            return Regex.IsMatch(
                normalizedMessage,
                @"\b(di|duong|lo\s*trinh|tu\s+.+\s+den\s+.+|toi\s+den\s+.+|di\s+qua\s+.+|qua\s+.+|route)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool HasExplicitRoutePayload(ChatbotAskRequest request, string? explicitOrigin, string? explicitDestination)
        {
            if (request.RoutePoints is { Count: >= 2 })
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(explicitOrigin)
                && !string.IsNullOrWhiteSpace(explicitDestination);
        }

        private static bool HasExplicitOriginSelection(ChatbotAskRequest request)
        {
            return IsMeaningfulLocation(request.Origin)
                || IsUsableCoordinate(request.OriginLatitude, request.OriginLongitude);
        }

        private static bool HasExplicitDestinationSelection(ChatbotAskRequest request)
        {
            return IsMeaningfulLocation(request.Destination)
                || IsUsableCoordinate(request.DestinationLatitude, request.DestinationLongitude);
        }

        private static bool IsMeaningfulLocation(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var normalized = VietnameseTextNormalizer.NormalizeForIntent(value).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            return normalized is not ("string" or "origin" or "destination" or "null" or "undefined" or "n/a" or "na" or "-" or "test");
        }

        private static string? ExtractDistrict(string normalizedMessage)
        {
            // 1. Cluster (cụm X)
            var clusterM = Regex.Match(normalizedMessage, @"\bcum\s*(?:thi\s*dua\s*)?(\d{1,2})\b");
            if (clusterM.Success)
                return VietnameseTextNormalizer.CanonicalizeDistrict($"cum {clusterM.Groups[1].Value}");

            // 2. Khu vực / quận có tên (chuẩn hóa đã xử lý "binh duong sap nhap", v.v.)
            foreach (var keyword in new[]
            {
                "thu duc", "binh duong", "ba ria", "vung tau",
                "tan binh", "phu nhuan", "binh tan", "tan phu",
                "binh thanh", "go vap", "hoc mon", "cu chi",
                "binh chanh", "nha be", "can gio"
            })
            {
                if (normalizedMessage.Contains(keyword))
                {
                    var canonical = VietnameseTextNormalizer.CanonicalizeDistrict(keyword);
                    if (canonical != null)
                        return canonical;
                }
            }

            // 3. Quận số (cũ) - "quan X"
            var match = Regex.Match(
                normalizedMessage,
                @"\bquan\s+(?<district>[0-9]{1,2}|[a-z0-9\s]{1,40})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!match.Success)
                return null;

            var district = match.Groups["district"].Value.Trim();
            district = Regex.Replace(district, @"\s+co\s+.*$", string.Empty, RegexOptions.IgnoreCase);
            district = Regex.Replace(district, @"\s+dang\s+.*$", string.Empty, RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(district) ? null : VietnameseTextNormalizer.CanonicalizeDistrict($"quan {district}");
        }

        private static bool LooksLikeRouteButMissingEndpoints(string normalizedMessage, ChatbotAskRequest request, out string? hintedDestination)
        {
            hintedDestination = null;

            if (!IsLikelyRouteRequest(normalizedMessage, request))
            {
                return false;
            }

            if (IsMeaningfulLocation(request.Origin) && IsMeaningfulLocation(request.Destination))
            {
                return false;
            }

            // Trường hợp đã có mẫu đầy đủ "tu ... den ..." thì để flow parse route xử lý.
            if (Regex.IsMatch(normalizedMessage, @"\btu\s+.+?\s+den\s+.+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return false;
            }

            // Bắt các câu kiểu "toi di toi/den quan 2..." -> route nhưng thiếu origin.
            var destinationMatch = Regex.Match(
                normalizedMessage,
                @"\b(?:di\s+)?(?:toi|den|qua)\s+(?<destination>quan\s+\d{1,2}|[a-z0-9\s]{2,80})",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            if (!destinationMatch.Success)
            {
                return false;
            }

            var rawDestination = destinationMatch.Groups["destination"].Value.Trim();
            rawDestination = Regex.Replace(
                rawDestination,
                @"\s+(thi\s+co\s+gap\s+mua\s+khong|thi\s+co\s+mua\s+khong|co\s+gap\s+mua\s+khong|co\s+mua\s+khong|mua\s+khong|khong)\b.*$",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            rawDestination = Regex.Replace(
                rawDestination,
                @"\s+thi$",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            rawDestination = Regex.Replace(
                rawDestination,
                @"^den\s+",
                string.Empty,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

            hintedDestination = VietnameseTextNormalizer.CanonicalizeLocation(rawDestination);
            return true;
        }

        private async Task<string?> ResolveCurrentOriginAsync(ChatbotAskRequest request, CancellationToken cancellationToken)
        {
            if (IsUsableCoordinate(request.CurrentLatitude, request.CurrentLongitude))
            {
                var currentLat = request.CurrentLatitude.GetValueOrDefault();
                var currentLng = request.CurrentLongitude.GetValueOrDefault();
                return $"{currentLat.ToString(CultureInfo.InvariantCulture)},{currentLng.ToString(CultureInfo.InvariantCulture)}";
            }

            if (request.UserId.HasValue)
            {
                var userLocation = await _dbContext.Users
                    .AsNoTracking()
                    .Where(u => u.Id == request.UserId.Value && u.LastKnownLocation != null)
                    .Select(u => new { u.LastKnownLocation, u.LocationUpdatedAt })
                    .FirstOrDefaultAsync(cancellationToken);

                if (userLocation?.LastKnownLocation != null)
                {
                    // Dữ liệu quá cũ dễ gây sai lệch route, nên chỉ dùng trong 24h gần nhất.
                    if (!userLocation.LocationUpdatedAt.HasValue || userLocation.LocationUpdatedAt.Value >= DateTime.UtcNow.AddHours(-24))
                    {
                        var lat = userLocation.LastKnownLocation.Y;
                        var lng = userLocation.LastKnownLocation.X;
                        return $"{lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}";
                    }
                }
            }

            return null;
        }

        private static string? ResolveExplicitOrigin(ChatbotAskRequest request)
        {
            if (IsUsableCoordinate(request.OriginLatitude, request.OriginLongitude))
            {
                var lat = request.OriginLatitude.GetValueOrDefault();
                var lng = request.OriginLongitude.GetValueOrDefault();
                return $"{lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}";
            }

            if (IsMeaningfulLocation(request.Origin))
            {
                return VietnameseTextNormalizer.CanonicalizeLocation(request.Origin!.Trim()) ?? request.Origin!.Trim();
            }

            return null;
        }

        private static string? ResolveExplicitDestination(ChatbotAskRequest request)
        {
            if (IsUsableCoordinate(request.DestinationLatitude, request.DestinationLongitude))
            {
                var lat = request.DestinationLatitude.GetValueOrDefault();
                var lng = request.DestinationLongitude.GetValueOrDefault();
                return $"{lat.ToString(CultureInfo.InvariantCulture)},{lng.ToString(CultureInfo.InvariantCulture)}";
            }

            if (IsMeaningfulLocation(request.Destination))
            {
                return VietnameseTextNormalizer.CanonicalizeLocation(request.Destination!.Trim()) ?? request.Destination!.Trim();
            }

            return null;
        }

        private static string? NormalizeLocationInput(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var normalized = VietnameseTextNormalizer.NormalizeForIntent(value).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return null;
            }

            if (normalized is "string" or "origin" or "destination" or "null" or "undefined" or "n/a" or "na" or "-")
            {
                return null;
            }

            return value.Trim();
        }

        private static bool IsUsableCoordinate(double? lat, double? lng)
        {
            if (!lat.HasValue || !lng.HasValue)
            {
                return false;
            }

            // Bo qua gia tri mac dinh Swagger 0,0.
            if (Math.Abs(lat.Value) < double.Epsilon && Math.Abs(lng.Value) < double.Epsilon)
            {
                return false;
            }

            return lat.Value is >= -90 and <= 90
                && lng.Value is >= -180 and <= 180;
        }

        private static bool IsUsableCoordinate(double lat, double lng)
        {
            if (Math.Abs(lat) < double.Epsilon && Math.Abs(lng) < double.Epsilon)
            {
                return false;
            }

            return lat is >= -90 and <= 90
                && lng is >= -180 and <= 180;
        }
    }
}
