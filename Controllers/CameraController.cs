using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;
using HcmcRainVision.Backend.Models.Enums;
using System.ComponentModel.DataAnnotations;

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

            // Validate WardId
            if (!string.IsNullOrEmpty(request.WardId))
            {
                var wardExists = await _context.Wards.AnyAsync(w => w.WardId == request.WardId);
                if (!wardExists)
                {
                    var availableWards = await _context.Wards.Select(w => w.WardId).ToListAsync();
                    return BadRequest(new 
                    { 
                        error = $"Ward '{request.WardId}' không tồn tại.",
                        availableWards = availableWards,
                        suggestion = "Vui lòng sử dụng một trong các Ward ID có sẵn hoặc để trống để tự động gán Ward 'DEFAULT'."
                    });
                }
            }
            else
            {
                // Gán Ward mặc định nếu không cung cấp
                request.WardId = "DEFAULT";
            }

            // Tạo Camera (không dùng SourceUrl cũ)
            var camera = new Camera
            {
                Id = request.Id,
                Name = request.Name,
                Latitude = request.Latitude,
                Longitude = request.Longitude,
                WardId = request.WardId,
                Status = nameof(CameraStatus.Active)
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
            
            // Trả về response không có navigation properties để tránh circular reference
            return Ok(new 
            { 
                camera = new 
                {
                    camera.Id,
                    camera.Name,
                    camera.Latitude,
                    camera.Longitude,
                    camera.WardId,
                    camera.Status
                },
                message = "Camera và Stream đã được tạo thành công" 
            });
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
            
            // Lưu ý: Ảnh của camera này (nếu lưu Local) vẫn tồn tại trong wwwroot/images/rain_logs/
            // Các ảnh này sẽ được dọn dẹp tự động sau 24h bởi CleanupOldImagesAsync() trong Worker
            // Nếu muốn xóa ngay lập tức, có thể thêm logic tìm và xóa các file có pattern {cameraId}_*
            
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
        [Required(ErrorMessage = "ID camera là bắt buộc.")]
        [StringLength(50, MinimumLength = 3, ErrorMessage = "ID phải từ 3-50 ký tự.")]
        public string Id { get; set; } = null!;

        [Required(ErrorMessage = "Tên camera là bắt buộc.")]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Tên phải từ 5-200 ký tự.")]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Vĩ độ là bắt buộc.")]
        [Range(10.0, 11.0, ErrorMessage = "Vĩ độ phải trong khoảng 10.0 - 11.0 (khu vực TP.HCM).")]
        public double Latitude { get; set; }

        [Required(ErrorMessage = "Kinh độ là bắt buộc.")]
        [Range(106.0, 107.0, ErrorMessage = "Kinh độ phải trong khoảng 106.0 - 107.0 (khu vực TP.HCM).")]
        public double Longitude { get; set; }

        [StringLength(50, ErrorMessage = "Ward ID tối đa 50 ký tự.")]
        public string? WardId { get; set; }

        [Required(ErrorMessage = "Stream URL là bắt buộc.")]
        [Url(ErrorMessage = "Stream URL phải là URL hợp lệ.")]
        public string StreamUrl { get; set; } = null!;

        [StringLength(20, ErrorMessage = "Stream Type tối đa 20 ký tự.")]
        public string? StreamType { get; set; }
    }

    public class UpdateCameraRequest
    {
        [Required(ErrorMessage = "Tên camera là bắt buộc.")]
        [StringLength(200, MinimumLength = 5, ErrorMessage = "Tên phải từ 5-200 ký tự.")]
        public string Name { get; set; } = null!;

        [Required(ErrorMessage = "Vĩ độ là bắt buộc.")]
        [Range(10.0, 11.0, ErrorMessage = "Vĩ độ phải trong khoảng 10.0 - 11.0.")]
        public double Latitude { get; set; }

        [Required(ErrorMessage = "Kinh độ là bắt buộc.")]
        [Range(106.0, 107.0, ErrorMessage = "Kinh độ phải trong khoảng 106.0 - 107.0.")]
        public double Longitude { get; set; }

        [StringLength(50, ErrorMessage = "Ward ID tối đa 50 ký tự.")]
        public string? WardId { get; set; }

        [StringLength(20, ErrorMessage = "Status tối đa 20 ký tự.")]
        public string? Status { get; set; }

        [Url(ErrorMessage = "Stream URL phải là URL hợp lệ.")]
        public string? StreamUrl { get; set; }
    }
}
