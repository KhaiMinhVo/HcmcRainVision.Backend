using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HcmcRainVision.Backend.Data;

namespace HcmcRainVision.Backend.Controllers
{
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
    }
}