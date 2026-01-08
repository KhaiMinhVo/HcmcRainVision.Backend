using Microsoft.ML;
using System.IO;

namespace HcmcRainVision.Backend.Services.AI
{
    public class RainPredictionService
    {
        private readonly string _modelPath;
        private readonly MLContext _mlContext;
        private ITransformer? _trainedModel;
        private PredictionEngine<ModelInput, ModelOutput>? _predictionEngine;
        private readonly ILogger<RainPredictionService> _logger;

        public RainPredictionService(IWebHostEnvironment env, ILogger<RainPredictionService> logger)
        {
            _logger = logger;
            _mlContext = new MLContext();
            
            // Đường dẫn file model (bạn sẽ ném file .zip vào thư mục gốc của dự án)
            _modelPath = Path.Combine(env.ContentRootPath, "RainAnalysisModel.zip");

            LoadModel();
        }

        private void LoadModel()
        {
            if (File.Exists(_modelPath))
            {
                try 
                {
                    // Load model từ file .zip
                    _trainedModel = _mlContext.Model.Load(_modelPath, out var modelInputSchema);
                    _predictionEngine = _mlContext.Model.CreatePredictionEngine<ModelInput, ModelOutput>(_trainedModel);
                    _logger.LogInformation("✅ Đã load Model AI thành công!");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"❌ Lỗi load model: {ex.Message}. Chuyển sang chế độ giả lập.");
                    _trainedModel = null;
                }
            }
            else
            {
                _logger.LogWarning("⚠️ Không tìm thấy file RainAnalysisModel.zip. Đang chạy chế độ GIẢ LẬP (Mock Mode).");
            }
        }

        public RainPredictionResult Predict(byte[] imageBytes)
        {
            // --- TRƯỜNG HỢP 1: CHƯA CÓ MODEL (Giả lập) ---
            if (_trainedModel == null || _predictionEngine == null)
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

            // --- TRƯỜNG HỢP 2: ĐÃ CÓ MODEL (Dự đoán thật) ---
            try
            {
                // Tạo input data
                var input = new ModelInput { Image = imageBytes };

                // Thực hiện dự đoán
                var result = _predictionEngine.Predict(input);

                // Giả sử nhãn của bạn là "Rain" và "NoRain"
                // Model trả về Score là mảng. Ví dụ: [0.1, 0.9] tương ứng [NoRain, Rain]
                // Logic này phụ thuộc vào lúc bạn train model gán nhãn nào trước.
                // Ở đây tôi giả định output đơn giản:
                
                bool isRaining = result.Prediction?.Equals("Rain", StringComparison.OrdinalIgnoreCase) ?? false;
                
                // Lấy độ tin cậy cao nhất trong mảng Score
                float maxScore = result.Score?.Max() ?? 0f; 

                return new RainPredictionResult
                {
                    IsRaining = isRaining,
                    Confidence = maxScore,
                    Message = "AI Prediction"
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