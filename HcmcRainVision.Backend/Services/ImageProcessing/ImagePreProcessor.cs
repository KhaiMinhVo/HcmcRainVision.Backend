using OpenCvSharp;

namespace HcmcRainVision.Backend.Services.ImageProcessing
{
    public interface IImagePreProcessor
    {
        /// <summary>
        /// Chuẩn hóa ảnh: Cắt bỏ thông tin thừa và resize về kích thước AI cần
        /// </summary>
        /// <param name="rawImageBytes">Ảnh gốc từ Crawler</param>
        /// <param name="targetWidth">Chiều rộng AI cần (thường là 224)</param>
        /// <param name="targetHeight">Chiều cao AI cần (thường là 224)</param>
        /// <returns>Ảnh đã xử lý dưới dạng byte[]</returns>
        byte[]? ProcessForAI(byte[] rawImageBytes, int targetWidth = 224, int targetHeight = 224);
    }

    public class ImagePreProcessor : IImagePreProcessor
    {
        private readonly ILogger<ImagePreProcessor> _logger;

        public ImagePreProcessor(ILogger<ImagePreProcessor> logger)
        {
            _logger = logger;
        }

        public byte[]? ProcessForAI(byte[] rawImageBytes, int targetWidth = 224, int targetHeight = 224)
        {
            try
            {
                // 1. Load ảnh từ byte array vào bộ nhớ OpenCV (Mat object)
                // Dùng 'using' để tự động giải phóng bộ nhớ (quan trọng vì OpenCV dùng C++ native memory)
                using var srcMat = Cv2.ImDecode(rawImageBytes, ImreadModes.Color);
                
                if (srcMat.Empty())
                {
                    _logger.LogWarning("Không thể decode ảnh (ảnh lỗi hoặc sai định dạng).");
                    return null;
                }

                // 2. Tính toán vùng cắt (ROI - Region of Interest)
                // Mục tiêu: Bỏ 15% trên (timestamp) và 10% dưới (logo)
                int cropY = (int)(srcMat.Rows * 0.15); // Bắt đầu từ 15% chiều cao
                int cropHeight = (int)(srcMat.Rows * 0.75); // Lấy 75% chiều cao giữa (100% - 15% trên - 10% dưới)
                
                // Đảm bảo không cắt lố khung hình
                if (cropY + cropHeight > srcMat.Rows) cropHeight = srcMat.Rows - cropY;

                var roi = new Rect(0, cropY, srcMat.Cols, cropHeight);

                // Thực hiện cắt
                using var croppedMat = new Mat(srcMat, roi);

                // 3. Resize về kích thước chuẩn của Model (thường là 224x224 hoặc 256x256)
                using var resizedMat = new Mat();
                Cv2.Resize(croppedMat, resizedMat, new Size(targetWidth, targetHeight), 0, 0, InterpolationFlags.Linear);

                // 4. Encode ngược lại thành byte[] để trả về (hoặc đưa vào ML.NET)
                // Dùng đuôi .jpg để nén nhẹ
                return resizedMat.ImEncode(".jpg");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Lỗi xử lý ảnh OpenCV: {ex.Message}");
                return null;
            }
        }
    }
}