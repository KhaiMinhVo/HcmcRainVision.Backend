using FirebaseAdmin;
using FirebaseAdmin.Messaging;
using Google.Apis.Auth.OAuth2;
using FcmNotification = FirebaseAdmin.Messaging.Notification; // Alias để tránh xung đột namespace

namespace HcmcRainVision.Backend.Services.Notification
{
    public interface IFirebasePushService
    {
        Task<bool> SendRainAlertAsync(string cameraName, string cameraId, double confidence);
        Task<bool> SendToDeviceAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null);
    }

    /// <summary>
    /// Service gửi Push Notification qua Firebase Cloud Messaging
    /// </summary>
    public class FirebasePushService : IFirebasePushService
    {
        private readonly ILogger<FirebasePushService> _logger;
        private readonly bool _isEnabled;

        public FirebasePushService(IConfiguration config, ILogger<FirebasePushService> logger)
        {
            _logger = logger;
            
            var credentialPath = config["FirebaseSettings:ServiceAccountPath"];
            
            // Kiểm tra xem Firebase có được cấu hình không
            _isEnabled = !string.IsNullOrEmpty(credentialPath) && File.Exists(credentialPath);

            if (_isEnabled)
            {
                try
                {
                    // Chỉ khởi tạo nếu chưa có app nào
                    if (FirebaseApp.DefaultInstance == null)
                    {
                        FirebaseApp.Create(new AppOptions()
                        {
                            Credential = GoogleCredential.FromFile(credentialPath)
                        });
                        _logger.LogInformation("✅ Firebase Admin SDK đã được khởi tạo thành công");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Không thể khởi tạo Firebase Admin SDK");
                    _isEnabled = false;
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Firebase chưa được cấu hình. Push notification sẽ bị vô hiệu hóa.");
            }
        }

        public async Task<bool> SendRainAlertAsync(string cameraName, string cameraId, double confidence)
        {
            if (!_isEnabled) return false;

            try
            {
                var message = new Message()
                {
                    Notification = new FcmNotification()
                    {
                        Title = "⚠️ Cảnh báo mưa!",
                        Body = $"Phát hiện mưa tại {cameraName} với độ tin cậy {confidence:P0}"
                    },
                    Data = new Dictionary<string, string>()
                    {
                        { "cameraId", cameraId },
                        { "cameraName", cameraName },
                        { "confidence", confidence.ToString() },
                        { "type", "rain_alert" }
                    },
                    Topic = "rain_alerts" // Gửi cho tất cả user đã subscribe topic này
                };

                string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                _logger.LogInformation($"✅ Đã gửi push notification: {response}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Lỗi gửi Firebase notification: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendToDeviceAsync(string deviceToken, string title, string body, Dictionary<string, string>? data = null)
        {
            if (!_isEnabled) return false;

            try
            {
                var message = new Message()
                {
                    Token = deviceToken,
                    Notification = new FcmNotification()
                    {
                        Title = title,
                        Body = body
                    },
                    Data = data ?? new Dictionary<string, string>()
                };

                string response = await FirebaseMessaging.DefaultInstance.SendAsync(message);
                _logger.LogInformation($"✅ Đã gửi notification đến device: {response}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"❌ Lỗi gửi notification đến device: {ex.Message}");
                return false;
            }
        }
    }
}
