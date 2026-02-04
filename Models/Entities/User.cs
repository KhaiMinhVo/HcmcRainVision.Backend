using System.ComponentModel.DataAnnotations;

namespace HcmcRainVision.Backend.Models.Entities
{
    public class User
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string PasswordHash { get; set; } = string.Empty; // Lưu mật khẩu đã mã hóa

        public string Role { get; set; } = "User"; // Phân quyền: "User" hoặc "Admin"

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Thêm 2 trường này để phục vụ Reset Password
        public string? ResetToken { get; set; }
        public DateTime? ResetTokenExpiry { get; set; }

        // 1. Thông tin cá nhân (Profile)
        public string? FullName { get; set; }
        public string? PhoneNumber { get; set; }
        public string? AvatarUrl { get; set; }

        // 2. Trạng thái (để Admin khóa tài khoản)
        public bool IsActive { get; set; } = true;

        // 3. Quan hệ: Danh sách Camera yêu thích
        public ICollection<FavoriteCamera> FavoriteCameras { get; set; } = new List<FavoriteCamera>();
        
        // 4. Quan hệ: Settings thông báo (Giữ cho tương thích ngược)
        public ICollection<UserNotificationSetting> UserNotificationSettings { get; set; } = new List<UserNotificationSetting>();
        
        // 5. Quan hệ: Đăng ký cảnh báo mới (AlertSubscription)
        public ICollection<AlertSubscription> AlertSubscriptions { get; set; } = new List<AlertSubscription>();
    }
}
