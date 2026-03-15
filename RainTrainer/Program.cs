using Microsoft.ML;
using Microsoft.ML.Vision;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RainTrainer
{
    // Dữ liệu đầu vào khớp với Backend
    public class ModelInput
    {
        public byte[] Image { get; set; } = Array.Empty<byte>();
        public string Label { get; set; } = string.Empty;
    }

    class Program
    {
        static async Task Main(string[] args)
        {
            await AutoDownloader.DownloadAuditImages();
            Console.WriteLine("---------------------------------------------------");

            var mlContext = new MLContext();
            
            // ĐƯỜNG DẪN TỚI THƯ MỤC DATASET (Thay đổi nếu cần)
            string datasetFolder = AutoDownloader.GetDatasetFolder();

            if (!Directory.Exists(datasetFolder))
            {
                Console.WriteLine($"❌ DatasetFolder không tồn tại: {datasetFolder}");
                return;
            }
            
            Console.WriteLine("1. Đang nạp ảnh từ thư mục...");
            var images = LoadImagesFromFolder(datasetFolder);
            var trainData = mlContext.Data.LoadFromEnumerable(images);

            // Xáo trộn dữ liệu để AI học đều hơn
            var shuffledData = mlContext.Data.ShuffleRows(trainData);

            Console.WriteLine("2. Đang thiết lập Pipeline...");
            var pipeline = mlContext.Transforms.Conversion.MapValueToKey("LabelAsKey", "Label")
                .Append(mlContext.MulticlassClassification.Trainers.ImageClassification(
                    featureColumnName: "Image",
                    labelColumnName: "LabelAsKey"))
                .Append(mlContext.Transforms.Conversion.MapKeyToValue("PredictedLabel", "LabelAsKey"));

            Console.WriteLine("3. Bắt đầu huấn luyện AI (Sẽ mất vài phút)...");
            var model = pipeline.Fit(shuffledData);

            Console.WriteLine("4. Huấn luyện xong! Đang lưu mô hình...");
            mlContext.Model.Save(model, trainData.Schema, "RainModel.zip");
            
            Console.WriteLine("🎉 THÀNH CÔNG! File RainModel.zip đã được tạo tại thư mục hiện tại.");
        }

        // Hàm đọc ảnh từ ổ cứng chuyển thành mảng Byte
        static IEnumerable<ModelInput> LoadImagesFromFolder(string folder)
        {
            var files = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                            f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
                
            foreach (var file in files)
            {
                var parentDir = Directory.GetParent(file);
                var label = parentDir?.Name ?? "Unknown";

                yield return new ModelInput 
                { 
                    Image = File.ReadAllBytes(file), 
                    Label = label 
                };
            }
        }
    }
}