using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;

namespace HcmcRainVision.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CameraController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CameraController(AppDbContext context)
        {
            _context = context;
        }

        // 1. Lấy danh sách camera (Public - Ai cũng xem được)
        [HttpGet]
        public async Task<IActionResult> GetCameras()
        {
            return Ok(await _context.Cameras.ToListAsync());
        }

        // 2. Thêm camera mới (Chỉ Admin)
        [Authorize(Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> AddCamera([FromBody] CreateCameraRequest request)
        {
            if (await _context.Cameras.AnyAsync(c => c.Id == request.Id))
            {
                return BadRequest("ID Camera này đã tồn tại.");
            }

            // Tạo Camera (không dùng SourceUrl cũ)
            var camera = new Camera
            {
                Id = request.Id,
                Name = request.Name,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                WardId = request.WardId,
                Status = "Active"
            };

            _context.Cameras.Add(camera);

            // Tạo CameraStream tương ứng
            if (!string.IsNullOrEmpty(request.StreamUrl))
            {
                var stream = new CameraStream
                {
                    CameraId = camera.Id,
                    StreamUrl = request.StreamUrl,
                    StreamType = request.StreamType ?? "Snapshot",
                    IsPrimary = true,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow
                };
                _context.CameraStreams.Add(stream);
            }

            await _context.SaveChangesAsync();
            return Ok(new { camera, message = "Camera và Stream đã được tạo thành công" });
        }

        // 3. Xóa camera (Chỉ Admin)
        [Authorize(Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCamera(string id)
        {
            var camera = await _context.Cameras.FindAsync(id);
            if (camera == null) return NotFound();

            _context.Cameras.Remove(camera);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã xóa camera thành công" });
        }
        
        // 4. Sửa thông tin camera (Chỉ Admin)
        [Authorize(Roles = "Admin")]
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCamera(string id, [FromBody] UpdateCameraRequest request)
        {
            var camera = await _context.Cameras
                .Include(c => c.Streams)
                .FirstOrDefaultAsync(c => c.Id == id);
                
            if (camera == null) return NotFound();

            // Cập nhật thông tin camera
            camera.Name = request.Name;
            camera.Latitude = request.Latitude;
            camera.Longitude = request.Longitude;
            camera.WardId = request.WardId;
            camera.Status = request.Status ?? camera.Status;

            // Cập nhật hoặc tạo stream mới
            if (!string.IsNullOrEmpty(request.StreamUrl))
            {
                var primaryStream = camera.Streams.FirstOrDefault(s => s.IsPrimary);
                if (primaryStream != null)
                {
                    primaryStream.StreamUrl = request.StreamUrl;
                    primaryStream.IsActive = true;
                }
                else
                {
                    _context.CameraStreams.Add(new CameraStream
                    {
                        CameraId = camera.Id,
                        StreamUrl = request.StreamUrl,
                        StreamType = "Snapshot",
                        IsPrimary = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            await _context.SaveChangesAsync();
            return Ok(camera);
        }
    }

    // DTOs for API requests
    public class CreateCameraRequest
    {
        public string Id { get; set; } = null!;
        public string Name { get; set; } = null!;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? WardId { get; set; }
        public string StreamUrl { get; set; } = null!;
        public string? StreamType { get; set; }
    }

    public class UpdateCameraRequest
    {
        public string Name { get; set; } = null!;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string? WardId { get; set; }
        public string? Status { get; set; }
        public string? StreamUrl { get; set; }
    }
}
