namespace HcmcRainVision.Backend.Models.DTOs
{
    public class RoutePointDto
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }

    // DTO cho check-route endpoint - hỗ trợ vị trí hiện tại của user
    public class CheckRouteRequest
    {
         // MODE 1: FE gửi GPS hiện tại + destination
         public double? CurrentLatitude { get; set; }
         public double? CurrentLongitude { get; set; }

         // MODE 2B: Chọn điểm đi/điểm đến bằng toạ độ pin trên bản đồ
         public double? OriginLatitude { get; set; }
         public double? OriginLongitude { get; set; }
         public double? DestinationLatitude { get; set; }
         public double? DestinationLongitude { get; set; }

         // Tuỳ chọn nâng cao: FE gửi sẵn polyline/điểm route
         // Nếu có và hợp lệ (>=2 điểm), backend dùng trực tiếp không cần build route.
         public List<RoutePointDto> RoutePoints { get; set; } = new();
    }

    public class DistrictRainResult
    {
        public string DistrictName { get; set; } = string.Empty;
        public int TotalCameras { get; set; }
        public int RainingCameras { get; set; }
        public double RainRatio { get; set; }
        public string Level { get; set; } = "no_data";
        public DateTime ObservedAtUtc { get; set; }
    }

    public class RouteRainResult
    {
        public bool IsSafe { get; set; }
        public double RiskScore { get; set; }
        public double RainyPointRatio { get; set; }
        public double AverageRainIntensity { get; set; }
        public double PeakRainIntensity { get; set; }
        public int SamplePointCount { get; set; }
        public int RainyPointCount { get; set; }
        public List<object> Warnings { get; set; } = new();

        // Metadata để FE hiển thị mode đang dùng
        public string? ModeUsed { get; set; }
        public bool HasExplicitOrigin { get; set; }
        public bool HasExplicitDestination { get; set; }
        public bool HasCurrentGpsOrigin { get; set; }
    }
}
