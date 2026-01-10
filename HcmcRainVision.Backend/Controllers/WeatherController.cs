using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
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
        // Cho phép người dùng báo cáo khi AI nhận diện sai
        [HttpPost("report")]
        public async Task<IActionResult> ReportIncorrectPrediction([FromBody] ReportDto input)
        {
            var report = new UserReport 
            {
                CameraId = input.CameraId,
                UserClaimIsRaining = input.IsRaining,
                Note = input.Note,
                Timestamp = DateTime.UtcNow
            };

            _context.UserReports.Add(report);
            await _context.SaveChangesAsync();
            
            return Ok(new { message = "Cảm ơn đóng góp của bạn! Hệ thống sẽ xem xét lại." });
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

            // 3. Kiểm tra va chạm không gian
            foreach (var log in rainingLogs)
            {
                if (log.Location == null) continue;

                // Tạo vùng đệm 1km quanh điểm mưa (0.009 độ ~ 1km)
                var rainZone = log.Location.Buffer(0.009); 

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
    }
}