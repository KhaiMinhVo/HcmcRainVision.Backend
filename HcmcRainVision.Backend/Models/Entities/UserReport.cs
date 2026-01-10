using System.ComponentModel.DataAnnotations;

namespace HcmcRainVision.Backend.Models.Entities
{
    public class UserReport
    {
        [Key]
        public int Id { get; set; }
        public string CameraId { get; set; } = null!;
        public bool UserClaimIsRaining { get; set; } // Người dùng bảo: Có mưa (True) / Không mưa (False)
        public DateTime Timestamp { get; set; }
        public string? Note { get; set; } // Ghi chú thêm
    }
}
