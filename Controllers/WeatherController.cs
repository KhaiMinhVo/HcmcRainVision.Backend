using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using System.Security.Claims;
using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;

namespace HcmcRainVision.Backend.Controllers
{
    // Class DTO để nhận dữ liệu từ Client
    public class ReportDto
    {
        public string CameraId { get; set; } = null!;
        public bool IsRaining { get; set; }
        public string? Note { get; set; }
    }
    public class RoutePointDto
    {
        public double Lat { get; set; }
        public double Lng { get; set; }
    }
    [ApiController]
    [Route("api/[controller]")]
    public class WeatherController : ControllerBase
    {
        private readonly AppDbContext _context;

        // Hằng số: Bán kính cảnh báo mưa (đơn vị: độ trong WGS84)
        // 0.009 độ ≈ 1km tại TP.HCM (vĩ độ ~10.8°)
        // LƯU Ý: Trong hệ tọa độ WGS84 (Lat/Long), Buffer tạo ra hình ellipse chứ không phải hình tròn đều
        // do kinh độ co lại khi lên cao vĩ độ. Với TP.HCM (gần xích đạo), sai số nhỏ và chấp nhận được.
        // Để chuẩn xác hơn, cần dùng hệ tọa độ phẳng (VN-2000/UTM) nhưng sẽ phức tạp hơn.
        private const double RAIN_ALERT_RADIUS_DEGREES = 0.009; // ~1km tại HCMC

        public WeatherController(AppDbContext context)
        {
            _context = context;
        }

        // API: GET api/weather/latest
        // Lấy dữ liệu mới nhất của các camera trong vòng 30 phút qua
        [HttpGet("latest")]
        public async Task<IActionResult> GetLatestWeather()
        {
            // Lấy mốc thời gian 30 phút trước
            var timeLimit = DateTime.UtcNow.AddMinutes(-30);

            var data = await _context.WeatherLogs
                .Where(x => x.Timestamp >= timeLimit)
                .OrderByDescending(x => x.Timestamp)
                .ToListAsync();

            // Chuyển đổi (Map) sang DTO cho Frontend dễ dùng
            var result = data.Select(x => new 
            {
                Id = x.Id,
                CameraId = x.CameraId,
                Latitude = x.Location?.Y ?? 0,  // Y là Vĩ độ
                Longitude = x.Location?.X ?? 0, // X là Kinh độ
                IsRaining = x.IsRaining,
                Confidence = x.Confidence,
                TimeAgo = GetTimeAgo(x.Timestamp)
            });

            return Ok(result);
        }

        // Hàm phụ trợ tính thời gian (VD: "5 phút trước")
        private string GetTimeAgo(DateTime timestamp)
        {
            var span = DateTime.UtcNow - timestamp;
            if (span.TotalMinutes < 1) return "Vừa xong";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} phút trước";
            return $"{(int)span.TotalHours} giờ trước";
        }

        // API: POST api/weather/report
        // Cho phép người dùng báo cáo khi AI nhận diện sai (Yêu cầu đăng nhập)
        [Authorize]
        [HttpPost("report")]
        public async Task<IActionResult> ReportIncorrectPrediction([FromBody] ReportDto input)
        {
            // Lấy thông tin người dùng từ Token đang đăng nhập
            var userIdStr = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            int? userId = userIdStr != null ? int.Parse(userIdStr) : null;

            var report = new UserReport 
            {
                CameraId = input.CameraId,
                UserClaimIsRaining = input.IsRaining,
                Note = input.Note,
                UserId = userId, // Lưu ID người dùng chuẩn chỉ
                Timestamp = DateTime.UtcNow
            };

            _context.UserReports.Add(report);
            await _context.SaveChangesAsync();
            
            return Ok(new { message = "Cảm ơn đóng góp của bạn!" });
        }

        // API: POST api/weather/check-route
        // Kiểm tra xem một lộ trình đi có cắt qua vùng đang mưa không
        [HttpPost("check-route")]
        public async Task<IActionResult> CheckRouteSafety([FromBody] List<RoutePointDto> routePoints)
        {
            if (routePoints == null || routePoints.Count < 2)
                return BadRequest("Lộ trình cần ít nhất 2 điểm.");

            // 1. Tạo đường dẫn (LineString) từ danh sách điểm
            var coordinates = routePoints.Select(p => new Coordinate(p.Lng, p.Lat)).ToArray();
            var routeLine = new LineString(coordinates);

            // 2. Lấy các điểm đang mưa trong 30 phút qua từ DB
            var timeLimit = DateTime.UtcNow.AddMinutes(-30);
            var rainingLogs = await _context.WeatherLogs
                .Where(x => x.IsRaining && x.Timestamp >= timeLimit)
                .Select(x => new { x.Location, x.CameraId })
                .ToListAsync();

            var warnings = new List<object>();

            // 3. Kiểm tra va chạm không gian (Spatial Intersection)
            foreach (var log in rainingLogs)
            {
                if (log.Location == null) continue;

                // Tạo vùng đệm (buffer) quanh điểm mưa
                // Sử dụng RAIN_ALERT_RADIUS_DEGREES (~1km tại TP.HCM)
                var rainZone = log.Location.Buffer(RAIN_ALERT_RADIUS_DEGREES); 

                // Kiểm tra xem lộ trình có đi qua vùng mưa không
                if (routeLine.Intersects(rainZone))
                {
                    warnings.Add(new { 
                        Lat = log.Location.Y, 
                        Lng = log.Location.X, 
                        Message = $"Mưa to gần Camera {log.CameraId}" 
                    });
                }
            }

            bool isSafe = warnings.Count == 0;
            return Ok(new { IsSafe = isSafe, Warnings = warnings });
        }

        // API: GET api/weather/heatmap
        // Lấy dữ liệu cho bản đồ nhiệt (Rain Heatmap)
        // Trả về danh sách điểm có mưa với trọng số dựa trên độ tin cậy của AI
        [HttpGet("heatmap")]
        public async Task<IActionResult> GetRainHeatmap()
        {
            var timeLimit = DateTime.UtcNow.AddMinutes(-30);
            
            var rainingLogs = await _context.WeatherLogs
                .Where(x => x.IsRaining && x.Timestamp >= timeLimit && x.Location != null)
                .Select(x => new 
                {
                    Lat = x.Location!.Y,      // Vĩ độ
                    Lng = x.Location!.X,      // Kinh độ
                    Intensity = x.Confidence  // Độ tin cậy làm cường độ nhiệt
                })
                .ToListAsync();

            return Ok(rainingLogs);
        }
    }
}