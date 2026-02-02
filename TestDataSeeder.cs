using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;
using NetTopologySuite.Geometries;

namespace HcmcRainVision.Backend;

public static class TestDataSeeder
{
    public static async Task SeedTestData(AppDbContext context)
    {
        // 1. Seed Cameras (Nếu chưa có)
        if (!context.Cameras.Any())
        {
            Console.WriteLine("📷 Đang thêm dữ liệu Camera mẫu...");
            var cameras = new[]
            {
                // Camera từ hệ thống giao thông TP.HCM
                new Camera 
                { 
                    Id = "CAM_Q1_001", 
                    Name = "Ngã tư Lê Duẩn - Pasteur (Q1)", 
                    SourceUrl = "TEST_MODE", // Thay bằng URL thật khi có
                    Latitude = 10.7797, 
                    Longitude = 106.6990 
                },
                new Camera 
                { 
                    Id = "CAM_Q1_002", 
                    Name = "Vòng xoay Quách Thị Trang (Q1)", 
                    SourceUrl = "TEST_MODE",
                    Latitude = 10.7712, 
                    Longitude = 106.6983 
                },
                new Camera 
                { 
                    Id = "CAM_Q3_001", 
                    Name = "Ngã tư CMT8 - Cách Mạng Tháng 8 (Q3)", 
                    SourceUrl = "TEST_MODE",
                    Latitude = 10.7785, 
                    Longitude = 106.6897 
                },
                new Camera 
                { 
                    Id = "CAM_Q5_001", 
                    Name = "Chợ An Đông (Q5)", 
                    SourceUrl = "TEST_MODE",
                    Latitude = 10.7550, 
                    Longitude = 106.6520 
                },
                new Camera 
                { 
                    Id = "CAM_Q7_001", 
                    Name = "Phú Mỹ Hưng (Q7)", 
                    SourceUrl = "TEST_MODE",
                    Latitude = 10.7290, 
                    Longitude = 106.7200 
                },
                new Camera 
                { 
                    Id = "CAM_BINHTAN_001", 
                    Name = "Cầu Bình Triệu (Bình Tân)", 
                    SourceUrl = "TEST_MODE",
                    Latitude = 10.8000, 
                    Longitude = 106.6300 
                },
                new Camera 
                { 
                    Id = "CAM_TAN_BINH_001", 
                    Name = "Sân bay Tân Sơn Nhất (Tân Bình)", 
                    SourceUrl = "TEST_MODE",
                    Latitude = 10.8185, 
                    Longitude = 106.6595 
                },
                new Camera 
                { 
                    Id = "CAM_TEST_01", 
                    Name = "Camera Test Mode (Bến Thành)", 
                    SourceUrl = "TEST_MODE", 
                    Latitude = 10.762622, 
                    Longitude = 106.660172 
                }
            };
            await context.Cameras.AddRangeAsync(cameras);
            await context.SaveChangesAsync();
            Console.WriteLine($"✅ Đã thêm {cameras.Length} cameras.");
        }

        // 2. Seed WeatherLogs (Nếu chưa có)
        if (context.WeatherLogs.Any())
        {
            Console.WriteLine("✅ Database đã có dữ liệu WeatherLogs, bỏ qua seeding.");
        }
        else
        {
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

        // --- 3. SEED USER ADMIN (MỚI) ---
        // Kiểm tra xem đã có admin chưa, nếu chưa thì tạo
        if (!context.Users.Any(u => u.Role == "Admin"))
        {
            Console.WriteLine("👤 Đang tạo tài khoản Admin mặc định...");
            
            // Mật khẩu mặc định: "admin123"
            // Lưu ý: Phải cài package 'BCrypt.Net-Next' trước đó
            string passwordHash = BCrypt.Net.BCrypt.HashPassword("admin123");

            var adminUser = new User
            {
                Username = "admin",
                Email = "admin@hcmcrain.com",
                PasswordHash = passwordHash,
                Role = "Admin", // Quyền cao nhất
                CreatedAt = DateTime.UtcNow
            };

            await context.Users.AddAsync(adminUser);
            await context.SaveChangesAsync();
            
            Console.WriteLine("✅ Đã tạo User: admin / admin123");
        }
    }
}
