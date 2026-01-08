using System.ComponentModel.DataAnnotations;
using NetTopologySuite.Geometries; // Thư viện xử lý bản đồ

namespace HcmcRainVision.Backend.Models.Entities
{
    public class WeatherLog
    {
        [Key]
        public int Id { get; set; }

        public string? CameraId { get; set; } // Ví dụ: "CAM_Q1_001"

        // Đây là kiểu dữ liệu đặc biệt của PostGIS
        // Nó lưu trữ kinh độ/vĩ độ chuẩn xác
        public Point? Location { get; set; } 

        public bool IsRaining { get; set; } // Kết quả từ AI

        public float Confidence { get; set; } // Độ tin cậy (0.0 - 1.0)

        public DateTime Timestamp { get; set; } // Thời điểm ghi nhận
    }
}