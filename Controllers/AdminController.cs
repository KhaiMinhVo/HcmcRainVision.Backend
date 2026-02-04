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
                .Include(c => c.Streams)
                .Where(c => !activeCameraIds.Contains(c.Id))
                .Select(c => new {
                    c.Id,
                    c.Name,
                    StreamUrl = c.Streams.FirstOrDefault(s => s.IsPrimary) != null ? c.Streams.FirstOrDefault(s => s.IsPrimary).StreamUrl : "N/A",
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

        // 7. Kiểm tra health (sức khỏe) của tất cả camera URLs
        [HttpGet("stats/check-camera-health")]
        public async Task<IActionResult> CheckCameraHealth()
        {
            var cameras = await _context.Cameras
                .Include(c => c.Streams)
                .ToListAsync();
            var results = new List<object>();
            
            using var httpClient = new HttpClient();
            
            // Cấu hình Header giống như CameraCrawler để tránh bị block
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            httpClient.DefaultRequestHeaders.Referrer = new Uri("http://giaothong.hochiminhcity.gov.vn/");
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            foreach (var cam in cameras)
            {
                var primaryStream = cam.Streams.FirstOrDefault(s => s.IsPrimary);
                if (primaryStream == null)
                {
                    results.Add(new {
                        Id = cam.Id,
                        Name = cam.Name,
                        Status = "No Stream",
                        StatusCode = 0,
                        ResponseTime = 0
                    });
                    continue;
                }
                
                // Bỏ qua camera test mode
                if (primaryStream.StreamUrl == "TEST_MODE")
                {
                    results.Add(new {
                        Id = cam.Id,
                        Name = cam.Name,
                        Status = "Test Mode",
                        StatusCode = 0,
                        ResponseTime = 0
                    });
                    continue;
                }

                var startTime = DateTime.UtcNow;
                try 
                {
                    var response = await httpClient.GetAsync(primaryStream.StreamUrl);
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    
                    // Kiểm tra content type
                    var contentType = response.Content.Headers.ContentType?.MediaType;
                    var isImage = contentType?.StartsWith("image/") ?? false;
                    
                    results.Add(new {
                        Id = cam.Id,
                        Name = cam.Name,
                        Status = response.IsSuccessStatusCode && isImage ? "Online" : "Invalid Response",
                        StatusCode = (int)response.StatusCode,
                        ResponseTime = Math.Round(responseTime, 0),
                        ContentType = contentType ?? "unknown"
                    });
                } 
                catch (Exception ex)
                {
                    var responseTime = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    results.Add(new { 
                        Id = cam.Id,
                        Name = cam.Name,
                        Status = "Error/Timeout",
                        StatusCode = 0,
                        ResponseTime = Math.Round(responseTime, 0),
                        Error = ex.Message
                    });
                }
            }
            
            var summary = new {
                TotalCameras = cameras.Count,
                Online = results.Count(r => r.GetType().GetProperty("Status")?.GetValue(r)?.ToString() == "Online"),
                Offline = results.Count(r => r.GetType().GetProperty("Status")?.GetValue(r)?.ToString() != "Online" && 
                                             r.GetType().GetProperty("Status")?.GetValue(r)?.ToString() != "Test Mode"),
                TestMode = results.Count(r => r.GetType().GetProperty("Status")?.GetValue(r)?.ToString() == "Test Mode"),
                CheckedAt = DateTime.UtcNow
            };
            
            return Ok(new { Summary = summary, Details = results });
        }

        // 6. Lấy lịch sử Ingestion Jobs (Tracking quét camera)
        [HttpGet("ingestion-jobs")]
        public async Task<IActionResult> GetIngestionJobs(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? status = null)
        {
            var query = _context.IngestionJobs
                .Include(j => j.Attempts)
                .AsQueryable();

            // Filter theo status nếu có
            if (!string.IsNullOrEmpty(status))
            {
                query = query.Where(j => j.Status == status);
            }

            var totalCount = await query.CountAsync();
            
            var jobs = await query
                .OrderByDescending(j => j.StartedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(j => new
                {
                    j.JobId,
                    j.JobType,
                    j.Status,
                    j.StartedAt,
                    j.EndedAt,
                    Duration = j.EndedAt.HasValue 
                        ? (j.EndedAt.Value - j.StartedAt).TotalSeconds 
                        : (double?)null,
                    j.Notes,
                    TotalAttempts = j.Attempts.Count,
                    SuccessfulAttempts = j.Attempts.Count(a => a.Status == "Success"),
                    FailedAttempts = j.Attempts.Count(a => a.Status == "Failed"),
                    AvgLatency = j.Attempts.Any() 
                        ? j.Attempts.Average(a => a.LatencyMs) 
                        : 0
                })
                .ToListAsync();

            return Ok(new
            {
                Page = page,
                PageSize = pageSize,
                TotalCount = totalCount,
                TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
                Jobs = jobs
            });
        }

        // 7. Lấy chi tiết một Ingestion Job (bao gồm tất cả attempts)
        [HttpGet("ingestion-jobs/{jobId}")]
        public async Task<IActionResult> GetIngestionJobDetails(Guid jobId)
        {
            var job = await _context.IngestionJobs
                .Include(j => j.Attempts)
                .Where(j => j.JobId == jobId)
                .Select(j => new
                {
                    j.JobId,
                    j.JobType,
                    j.Status,
                    j.StartedAt,
                    j.EndedAt,
                    Duration = j.EndedAt.HasValue 
                        ? (j.EndedAt.Value - j.StartedAt).TotalSeconds 
                        : (double?)null,
                    j.Notes,
                    Attempts = j.Attempts.Select(a => new
                    {
                        a.AttemptId,
                        a.CameraId,
                        a.Status,
                        a.LatencyMs,
                        a.HttpStatus,
                        a.ErrorMessage,
                        a.AttemptAt
                    }).OrderBy(a => a.AttemptAt).ToList()
                })
                .FirstOrDefaultAsync();

            if (job == null)
                return NotFound(new { Message = "Job not found" });

            return Ok(job);
        }

        // 8. Thống kê Ingestion Performance
        [HttpGet("ingestion-stats")]
        public async Task<IActionResult> GetIngestionStats([FromQuery] int days = 7)
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-days);

            var jobs = await _context.IngestionJobs
                .Include(j => j.Attempts)
                .Where(j => j.StartedAt >= cutoffDate)
                .ToListAsync();

            var totalJobs = jobs.Count;
            var completedJobs = jobs.Count(j => j.Status == "Completed");
            var failedJobs = jobs.Count(j => j.Status == "Failed");

            var allAttempts = jobs.SelectMany(j => j.Attempts).ToList();
            var totalAttempts = allAttempts.Count;
            var successfulAttempts = allAttempts.Count(a => a.Status == "Success");
            var failedAttempts = allAttempts.Count(a => a.Status == "Failed");

            // Camera có tỷ lệ lỗi cao nhất
            var cameraFailureStats = allAttempts
                .GroupBy(a => a.CameraId)
                .Select(g => new
                {
                    CameraId = g.Key,
                    TotalAttempts = g.Count(),
                    FailedAttempts = g.Count(a => a.Status == "Failed"),
                    FailureRate = g.Count() > 0 
                        ? (double)g.Count(a => a.Status == "Failed") / g.Count() * 100 
                        : 0,
                    AvgLatency = g.Average(a => a.LatencyMs)
                })
                .OrderByDescending(x => x.FailureRate)
                .Take(10)
                .ToList();

            return Ok(new
            {
                Period = $"Last {days} days",
                Jobs = new
                {
                    Total = totalJobs,
                    Completed = completedJobs,
                    Failed = failedJobs,
                    SuccessRate = totalJobs > 0 ? Math.Round((double)completedJobs / totalJobs * 100, 2) : 0
                },
                Attempts = new
                {
                    Total = totalAttempts,
                    Successful = successfulAttempts,
                    Failed = failedAttempts,
                    SuccessRate = totalAttempts > 0 ? Math.Round((double)successfulAttempts / totalAttempts * 100, 2) : 0,
                    AvgLatency = allAttempts.Any() ? Math.Round(allAttempts.Average(a => a.LatencyMs), 0) : 0
                },
                ProblematicCameras = cameraFailureStats
            });
        }
    }
}
