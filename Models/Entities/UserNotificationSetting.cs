namespace HcmcRainVision.Backend.Models.Entities
{
    /// <summary>
    /// Lưu trữ cài đặt thông báo cho người dùng
    /// Dùng để gửi Push Notification qua Firebase Cloud Messaging
    /// </summary>
    public class UserNotificationSetting
    {
        public int Id { get; set; }
        
        /// <summary>
        /// ID người dùng (Foreign Key)
        /// </summary>
        public int UserId { get; set; }
        
        /// <summary>
        /// Firebase Cloud Messaging Token của thiết bị
        /// </summary>
        public string DeviceToken { get; set; } = null!;
        
        /// <summary>
        /// Danh sách quận/huyện quan tâm (ngăn cách bởi dấu phẩy)
        /// Ví dụ: "Quận 1, Quận 3, Quận 7"
        /// </summary>
        public string InterestedDistricts { get; set; } = "";
        
        /// <summary>
        /// Bật/tắt nhận thông báo
        /// </summary>
        public bool IsEnabled { get; set; } = true;
        
        /// <summary>
        /// Thời gian đăng ký
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        // Navigation property
        public User? User { get; set; }
    }
}
