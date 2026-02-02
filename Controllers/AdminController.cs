using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.DTOs;

namespace HcmcRainVision.Backend.Controllers
{
    [Authorize(Roles = "Admin")] // Chỉ Admin mới vào được
    [Route("api/[controller]")]
    [ApiController]
    public class AdminController : ControllerBase
    {
        private readonly AppDbContext _context;

        public AdminController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Thống kê hệ thống
        [HttpGet("stats")]
        public async Task<IActionResult> GetSystemStats()
        {
            var totalCameras = await _context.Cameras.CountAsync();
            var totalLogs = await _context.WeatherLogs.CountAsync();
            var totalReports = await _context.UserReports.CountAsync();
            
            // Lấy lần quét cuối cùng
            var lastScan = await _context.WeatherLogs
                .OrderByDescending(x => x.Timestamp)
                .Select(x => x.Timestamp)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                TotalCameras = totalCameras,
                TotalWeatherLogs = totalLogs,
                TotalUserReports = totalReports,
                LastSystemScan = lastScan,
                SystemStatus = "Running"
            });
        }

        // 2. Lấy danh sách ảnh cần kiểm tra lại (User báo sai)
        // Logic: Tìm các UserReport, sau đó tìm WeatherLog tương ứng (dựa trên CameraId và thời gian gần nhất)
        [HttpGet("audit-data")]
        public async Task<IActionResult> GetAuditData()
        {
            var reports = await _context.UserReports
                .OrderByDescending(r => r.Timestamp)
                .Take(100) // Lấy 100 báo cáo mới nhất
                .ToListAsync();

            var result = new List<object>();

            foreach (var report in reports)
            {
                // Tìm log của AI trong khoảng +- 5 phút so với lúc user báo cáo (dùng UTC)
                var relevantLog = await _context.WeatherLogs
                    .Where(w => w.CameraId == report.CameraId 
                                && w.Timestamp >= report.Timestamp.AddMinutes(-5)
                                && w.Timestamp <= report.Timestamp.AddMinutes(5))
                    .OrderBy(w => Math.Abs((w.Timestamp - report.Timestamp).Ticks)) // Lấy log gần nhất
                    .FirstOrDefaultAsync();

                if (relevantLog != null)
                {
                    result.Add(new
                    {
                        ReportId = report.Id,
                        CameraId = report.CameraId,
                        UserSaid = report.UserClaimIsRaining ? "Rain" : "No Rain",
                        AISaid = relevantLog.IsRaining ? "Rain" : "No Rain",
                        AIConfidence = relevantLog.Confidence,
                        ImageUrl = relevantLog.ImageUrl, // Ảnh này sẽ dùng để train lại
                        ReportTime = report.Timestamp,
                        Note = report.Note
                    });
                }
            }

            return Ok(result);
        }

        // 3. Quản lý User - Lấy danh sách tất cả user
        [HttpGet("users")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _context.Users
                .Select(u => new UserAdminViewDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    FullName = u.FullName,
                    Role = u.Role,
                    IsActive = u.IsActive,
                    CreatedAt = u.CreatedAt
                })
                .ToListAsync();
            return Ok(users);
        }

        // 4. Khóa/Mở khóa tài khoản user
        [HttpPut("users/{id}/ban")]
        public async Task<IActionResult> ToggleBanUser(int id)
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null) return NotFound();
            
            if (user.Role == "Admin") return BadRequest("Không thể khóa tài khoản Admin!");

            user.IsActive = !user.IsActive; // Đảo ngược trạng thái (Khóa <-> Mở)
            await _context.SaveChangesAsync();

            string status = user.IsActive ? "Đã mở khóa" : "Đã khóa";
            return Ok(new { message = $"{status} tài khoản {user.Username}" });
        }

        // 5. Thống kê tần suất mưa theo giờ
        [HttpGet("stats/rain-frequency")]
        public async Task<IActionResult> GetRainFrequency()
        {
            // Thống kê số lượng bản ghi mưa theo từng giờ trong 7 ngày qua
            var weekAgo = DateTime.UtcNow.AddDays(-7);
            
            var stats = await _context.WeatherLogs
                .Where(x => x.IsRaining && x.Timestamp >= weekAgo)
                .GroupBy(x => x.Timestamp.Hour)
                .Select(g => new { Hour = g.Key, Count = g.Count() })
                .OrderBy(x => x.Hour)
                .ToListAsync();

            return Ok(stats);
        }

        // 6. Lấy danh sách camera lỗi (không có dữ liệu trong 1 giờ qua)
        [HttpGet("stats/failed-cameras")]
        public async Task<IActionResult> GetFailedCameras()
        {
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            
            // Lấy danh sách camera có dữ liệu mới
            var activeCameraIds = await _context.WeatherLogs
                .Where(x => x.Timestamp > oneHourAgo)
                .Select(x => x.CameraId)
                .Distinct()
                .ToListAsync();

            // Lấy camera KHÔNG có trong danh sách active
            var failedCameras = await _context.Cameras
                .Where(c => !activeCameraIds.Contains(c.Id))
                .Select(c => new {
                    c.Id,
                    c.Name,
                    c.SourceUrl,
                    c.Latitude,
                    c.Longitude,
                    Status = "Offline - Không có dữ liệu mới"
                })
                .ToListAsync();

            return Ok(new {
                TotalFailed = failedCameras.Count,
                Cameras = failedCameras
            });
        }
    }
}
