using System.ComponentModel.DataAnnotations;

namespace HcmcRainVision.Backend.Models.Entities
{
    public class Camera
    {
        [Key]
        public string Id { get; set; } = null!; // Ví dụ: "CAM_Q1_001"

        public string Name { get; set; } = null!; // Ví dụ: "Camera Ngã 6 Phù Đổng"

        public string SourceUrl { get; set; } = null!; // Link ảnh snapshot

        public double Latitude { get; set; } // Vĩ độ
        public double Longitude { get; set; } // Kinh độ

        public DateTime? LastRainAlertSent { get; set; } // Thời gian gửi cảnh báo mưa lần cuối
    }
}
