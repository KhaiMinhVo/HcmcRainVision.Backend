using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;
using NetTopologySuite.Geometries;

namespace HcmcRainVision.Backend;

public static class TestDataSeeder
{
    public static async Task SeedTestData(AppDbContext context)
    {
        // Kiểm tra đã có dữ liệu chưa
        if (context.WeatherLogs.Any())
        {
            Console.WriteLine("✅ Database đã có dữ liệu, bỏ qua seeding.");
            return;
        }

        Console.WriteLine("🌱 Bắt đầu seed dữ liệu test...");

        var testData = new[]
        {
            new WeatherLog
            {
                CameraId = "CAM_BenThanh",
                Location = new Point(106.6983, 10.7721) { SRID = 4326 },
                IsRaining = true,
                Confidence = 0.87f,
                Timestamp = DateTime.UtcNow.AddMinutes(-5)
            },
            new WeatherLog
            {
                CameraId = "CAM_NhaThoDucBa",
                Location = new Point(106.6990, 10.7797) { SRID = 4326 },
                IsRaining = false,
                Confidence = 0.92f,
                Timestamp = DateTime.UtcNow.AddMinutes(-10)
            },
            new WeatherLog
            {
                CameraId = "CAM_PhoNguyen",
                Location = new Point(106.6950, 10.7650) { SRID = 4326 },
                IsRaining = true,
                Confidence = 0.78f,
                Timestamp = DateTime.UtcNow.AddMinutes(-15)
            },
            new WeatherLog
            {
                CameraId = "CAM_QuanTan",
                Location = new Point(106.7050, 10.7850) { SRID = 4326 },
                IsRaining = false,
                Confidence = 0.95f,
                Timestamp = DateTime.UtcNow.AddMinutes(-20)
            }
        };

        await context.WeatherLogs.AddRangeAsync(testData);
        await context.SaveChangesAsync();

        Console.WriteLine($"✅ Đã thêm {testData.Length} bản ghi test vào database.");
    }
}
