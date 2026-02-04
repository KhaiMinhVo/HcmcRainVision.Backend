namespace HcmcRainVision.Backend.Models.Constants
{
    /// <summary>
    /// Hằng số toàn cục cho ứng dụng - Tránh hardcoded strings và magic numbers
    /// </summary>
    public static class AppConstants
    {
        /// <summary>
        /// Các vai trò người dùng trong hệ thống
        /// </summary>
        public static class UserRoles
        {
            public const string Admin = "Admin";
            public const string User = "User";
        }

        /// <summary>
        /// Các nhóm SignalR để broadcast real-time alerts
        /// </summary>
        public static class SignalRGroups
        {
            /// <summary>
            /// Nhóm Dashboard cho Admin xem tổng quan tất cả alerts
            /// </summary>
            public const string Dashboard = "Dashboard";
            
            /// <summary>
            /// Phương thức SignalR để gửi alert xuống client
            /// </summary>
            public const string ReceiveRainAlertMethod = "ReceiveRainAlert";
        }

        /// <summary>
        /// Ngưỡng và cấu hình AI Prediction
        /// </summary>
        public static class AiPrediction
        {
            /// <summary>
            /// Ngưỡng tin cậy tối thiểu để coi dự đoán là chắc chắn.
            /// Nếu < 0.6, AI không chắc chắn -> cần lưu ảnh để review
            /// </summary>
            public const double LowConfidenceThreshold = 0.6;
        }

        /// <summary>
        /// Cấu hình thời gian cho các tính năng
        /// </summary>
        public static class Timing
        {
            /// <summary>
            /// Thời gian cooldown giữa 2 lần gửi thông báo mưa cho cùng một camera (phút)
            /// Tránh spam notification khi mưa kéo dài
            /// </summary>
            public const int RainAlertCooldownMinutes = 30;
            
            /// <summary>
            /// Chu kỳ quét camera (phút)
            /// </summary>
            public const int CameraScanIntervalMinutes = 5;
        }

        /// <summary>
        /// Loại Job trong hệ thống
        /// </summary>
        public static class JobTypes
        {
            public const string RainScan = "RainScan";
        }
        
        /// <summary>
        /// Firebase Cloud Messaging Topics
        /// </summary>
        public static class Topics
        {
            /// <summary>
            /// Topic để gửi cảnh báo mưa cho tất cả users đã subscribe
            /// </summary>
            public const string RainAlerts = "rain_alerts";
        }
    }
}
