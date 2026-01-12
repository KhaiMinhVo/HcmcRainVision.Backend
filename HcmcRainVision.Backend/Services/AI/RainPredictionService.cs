using Microsoft.ML;
using Microsoft.Extensions.ML;
using System.IO;

namespace HcmcRainVision.Backend.Services.AI
{
    public class RainPredictionService
    {
        private readonly PredictionEnginePool<ModelInput, ModelOutput>? _predictionEnginePool;
        private readonly ILogger<RainPredictionService> _logger;
        private readonly bool _isMockMode;

        public RainPredictionService(
            ILogger<RainPredictionService> logger,
            IWebHostEnvironment env)
        {
            _logger = logger;
            
            // Kiểm tra xem có file model không
            var modelPath = Path.Combine(env.ContentRootPath, "RainAnalysisModel.zip");
            _isMockMode = !File.Exists(modelPath);

            if (_isMockMode)
            {
                _logger.LogWarning("⚠️ Không tìm thấy file RainAnalysisModel.zip. Đang chạy chế độ GIẢ LẬP (Mock Mode).");
            }
            else
            {
                _logger.LogInformation("✅ Phát hiện Model AI, sẵn sàng sử dụng PredictionEnginePool.");
            }
        }

        // Constructor overload để inject PredictionEnginePool (khi có model)
        public RainPredictionService(
            PredictionEnginePool<ModelInput, ModelOutput> predictionEnginePool,
            ILogger<RainPredictionService> logger)
        {
            _predictionEnginePool = predictionEnginePool;
            _logger = logger;
            _isMockMode = false;
        }

        public RainPredictionResult Predict(byte[] imageBytes)
        {
            // --- TRƯỜNG HỢP 1: CHƯA CÓ MODEL (Giả lập) ---
            if (_isMockMode || _predictionEnginePool == null)
            {
                // Random kết quả để test giao diện
                var random = new Random();
                bool isRain = random.NextDouble() > 0.5; // 50/50
                return new RainPredictionResult
                {
                    IsRaining = isRain,
                    Confidence = (float)(0.7 + random.NextDouble() * 0.2), // Random từ 70% - 90%
                    Message = "[MOCK] Dữ liệu giả lập"
                };
            }

            // --- TRƯỜNG HỢP 2: ĐÃ CÓ MODEL (Dự đoán thật với PredictionEnginePool - Thread-safe) ---
            try
            {
                var input = new ModelInput { Image = imageBytes };
                
                // Không cần lock, Pool tự xử lý thread-safe
                var result = _predictionEnginePool.Predict(input);

                // Giả sử nhãn của bạn là "Rain" và "NoRain"
                bool isRaining = result.Prediction?.Equals("Rain", StringComparison.OrdinalIgnoreCase) ?? false;
                
                // Lấy độ tin cậy cao nhất trong mảng Score
                float maxScore = result.Score?.Max() ?? 0f; 

                return new RainPredictionResult
                {
                    IsRaining = isRaining,
                    Confidence = maxScore,
                    Message = "AI Prediction (Pool)"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError($"Lỗi khi dự đoán: {ex.Message}");
                return new RainPredictionResult { IsRaining = false, Confidence = 0, Message = "Error" };
            }
        }
    }
}