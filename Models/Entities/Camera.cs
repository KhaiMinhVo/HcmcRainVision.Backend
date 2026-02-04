using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HcmcRainVision.Backend.Models.Entities
{
    public class Camera
    {
        [Key]
        public string Id { get; set; } = null!; 

        public string Name { get; set; } = null!; 
        public double Latitude { get; set; } 
        public double Longitude { get; set; } 

        // --- LIÊN KẾT MỚI ---
        public string? WardId { get; set; }
        
        [ForeignKey("WardId")]
        public Ward? Ward { get; set; }

        public ICollection<CameraStream> Streams { get; set; } = new List<CameraStream>();
        public ICollection<CameraStatusLog> StatusLogs { get; set; } = new List<CameraStatusLog>();

        public string Status { get; set; } = "Active";

        // --- CÁC TRƯỜNG CŨ (TẠM GIỮ ĐỂ MIGRATE, SẼ XÓA SAU) ---
        [Obsolete("Use CameraStream.StreamUrl instead")]
        public string SourceUrl { get; set; } = null!; 
        
        [Obsolete("No longer needed with AlertSubscription system")]
        public DateTime? LastRainAlertSent { get; set; }
    }
}
