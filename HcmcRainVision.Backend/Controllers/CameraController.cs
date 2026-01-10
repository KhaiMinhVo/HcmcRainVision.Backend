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

        // 1. Lấy danh sách camera
        [HttpGet]
        public async Task<IActionResult> GetCameras()
        {
            return Ok(await _context.Cameras.ToListAsync());
        }

        // 2. Thêm camera mới
        [HttpPost]
        public async Task<IActionResult> AddCamera([FromBody] Camera camera)
        {
            if (await _context.Cameras.AnyAsync(c => c.Id == camera.Id))
            {
                return BadRequest("ID Camera này đã tồn tại.");
            }

            _context.Cameras.Add(camera);
            await _context.SaveChangesAsync();
            return Ok(camera);
        }

        // 3. Xóa camera
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCamera(string id)
        {
            var camera = await _context.Cameras.FindAsync(id);
            if (camera == null) return NotFound();

            _context.Cameras.Remove(camera);
            await _context.SaveChangesAsync();
            return Ok(new { message = "Đã xóa camera thành công" });
        }
        
        // 4. Sửa thông tin camera (VD: đổi URL stream)
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCamera(string id, [FromBody] Camera updatedCam)
        {
            var camera = await _context.Cameras.FindAsync(id);
            if (camera == null) return NotFound();

            camera.Name = updatedCam.Name;
            camera.SourceUrl = updatedCam.SourceUrl;
            camera.Latitude = updatedCam.Latitude;
            camera.Longitude = updatedCam.Longitude;

            await _context.SaveChangesAsync();
            return Ok(camera);
        }
    }
}
