using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.Models.Entities;
using HcmcRainVision.Backend.Models.Enums;
using HcmcRainVision.Backend.Models.Constants;
using NetTopologySuite.Geometries;
using Microsoft.EntityFrameworkCore;

namespace HcmcRainVision.Backend;

public static class TestDataSeeder
{
    public static async Task SeedTestData(AppDbContext context)
    {
        // --- 0. MIGRATE DỮ LIỆU CŨ (Chạy trước khi seed) ---
        await MigrateOldData(context);

        // --- 1. SEED WARDS ---
        // Chỉ seed khi chưa có Ward nghiệp vụ (bỏ qua Ward DEFAULT do migrate tạo ra)
        if (!await context.Wards.AnyAsync(w => w.WardId != "DEFAULT"))
        {
            Console.WriteLine("🏘️ Đang thêm dữ liệu Ward mẫu (Đã cập nhật theo hướng Phường)...");
            var wards = new[]
            {
                new Ward { WardId = "W_THUTHIEM_TD", WardName = "Phường Thủ Thiêm", DistrictName = "TP. Thủ Đức", Alias = "Sáp nhập từ một phần khu đô thị Thủ Thiêm cũ" },
                new Ward { WardId = "W_ANLOIDONG_TD", WardName = "Phường An Lợi Đông", DistrictName = "TP. Thủ Đức", Alias = "Khu vực An Lợi Đông cũ" },
                new Ward { WardId = "W_THAODIEN_TD", WardName = "Phường Thảo Điền", DistrictName = "TP. Thủ Đức", Alias = "Khu vực Thảo Điền cũ" },
                new Ward { WardId = "W_VTS_Q3", WardName = "Phường Võ Thị Sáu", DistrictName = "Quận 3", Alias = "Gồm P.6, P.7, P.8 cũ" },
                new Ward { WardId = "W_BENNGHE_Q1", WardName = "Phường Bến Nghé", DistrictName = "Quận 1", Alias = "Khu vực trung tâm Q1" },
                new Ward { WardId = "W_BENTHANH_Q1", WardName = "Phường Bến Thành", DistrictName = "Quận 1", Alias = "Khu vực chợ Bến Thành" },
                new Ward { WardId = "W_KHANHHOI_Q4", WardName = "Phường Khánh Hội", DistrictName = "Quận 4", Alias = "Khu vực P.2, P.5 Q4 cũ" },
                new Ward { WardId = "W_CHOQUAN_Q5", WardName = "Phường Chợ Quán", DistrictName = "Quận 5", Alias = "Khu vực P.1, P.2 Q5 cũ" }
            };

            await context.Wards.AddRangeAsync(wards);
            await context.SaveChangesAsync();
            Console.WriteLine($"✅ Đã thêm {wards.Length} wards cập nhật.");
        }

        // 2. Seed Cameras (Nếu chưa có)
        if (!context.Cameras.Any())
        {
            Console.WriteLine("📷 Đang thêm dữ liệu Camera mẫu...");
            var cameras = new[]
            {
                // ===============================================
                // HƯỚNG DẪN LẤY URL CAMERA THẬT:
                // 1. Vào: http://giaothong.hochiminhcity.gov.vn
                // 2. Click vào bản đồ, chọn camera
                // 3. Chuột phải vào ảnh → "Open image in new tab"
                // 4. Copy URL có dạng: .../ImageHandler.ashx?id=...
                // ===============================================
                
                // Camera thật từ hệ thống giao thông TP.HCM (thay ?id=... bằng ID thật)
                new Camera 
                { 
                    Id = "CAM_Q1_001", 
                    Name = "Ngã tư Lê Duẩn - Pasteur (Q1)",
                    Latitude = 10.7797, 
                    Longitude = 106.6990,
                    WardId = "W_BENNGHE_Q1",
                    Status = nameof(CameraStatus.Active)
                },
                new Camera 
                { 
                    Id = "CAM_Q1_002", 
                    Name = "Vòng xoay Quách Thị Trang (Q1)",
                    Latitude = 10.7712, 
                    Longitude = 106.6983,
                    WardId = "W_BENTHANH_Q1",
                    Status = nameof(CameraStatus.Active)
                },
                new Camera 
                { 
                    Id = "CAM_Q3_001", 
                    Name = "Ngã tư CMT8 - Cách Mạng Tháng 8 (Q3)",
                    Latitude = 10.7785, 
                    Longitude = 106.6897,
                    WardId = "W_VTS_Q3",
                    Status = nameof(CameraStatus.Active)
                },
                new Camera 
                { 
                    Id = "CAM_Q5_001", 
                    Name = "Chợ An Đông (Q5)",
                    Latitude = 10.7550, 
                    Longitude = 106.6520,
                    WardId = "W_CHOQUAN_Q5",
                    Status = nameof(CameraStatus.Active)
                },
                new Camera 
                { 
                    Id = "CAM_Q7_001", 
                    Name = "Phú Mỹ Hưng (Q7)",
                    Latitude = 10.7290, 
                    Longitude = 106.7200,
                    WardId = "W_THUTHIEM_TD",
                    Status = nameof(CameraStatus.Active)
                },
                new Camera 
                { 
                    Id = "CAM_BINHTAN_001", 
                    Name = "Cầu Bình Triệu (Bình Tân)",
                    Latitude = 10.8000, 
                    Longitude = 106.6300,
                    WardId = "W_KHANHHOI_Q4",
                    Status = nameof(CameraStatus.Active)
                },
                new Camera 
                { 
                    Id = "CAM_TAN_BINH_001", 
                    Name = "Sân bay Tân Sơn Nhất (Tân Bình)",
                    Latitude = 10.8185, 
                    Longitude = 106.6595,
                    WardId = "W_BENNGHE_Q1",
                    Status = nameof(CameraStatus.Active)
                },
                // Camera TEST MODE (fallback khi không có camera thật)
                new Camera 
                { 
                    Id = "CAM_TEST_01", 
                    Name = "Camera Test Mode (Bến Thành)",
                    Latitude = 10.762622, 
                    Longitude = 106.660172,
                    WardId = "W_BENTHANH_Q1",
                    Status = nameof(CameraStatus.Active)
                }
            };
            await context.Cameras.AddRangeAsync(cameras);
            await context.SaveChangesAsync();
            Console.WriteLine($"✅ Đã thêm {cameras.Length} cameras.");
            
            // Tạo CameraStream cho mỗi camera
            var streams = new[]
            {
                new CameraStream { CameraId = "CAM_Q1_001", StreamUrl = "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f707", StreamType = "Snapshot", IsPrimary = true, IsActive = true },
                new CameraStream { CameraId = "CAM_Q1_002", StreamUrl = "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f708", StreamType = "Snapshot", IsPrimary = true, IsActive = true },
                new CameraStream { CameraId = "CAM_Q3_001", StreamUrl = "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f709", StreamType = "Snapshot", IsPrimary = true, IsActive = true },
                new CameraStream { CameraId = "CAM_Q5_001", StreamUrl = "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f70a", StreamType = "Snapshot", IsPrimary = true, IsActive = true },
                new CameraStream { CameraId = "CAM_Q7_001", StreamUrl = "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f70b", StreamType = "Snapshot", IsPrimary = true, IsActive = true },
                new CameraStream { CameraId = "CAM_BINHTAN_001", StreamUrl = "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f70c", StreamType = "Snapshot", IsPrimary = true, IsActive = true },
                new CameraStream { CameraId = "CAM_TAN_BINH_001", StreamUrl = "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f70d", StreamType = "Snapshot", IsPrimary = true, IsActive = true },
                new CameraStream { CameraId = "CAM_TEST_01", StreamUrl = AppConstants.Camera.TestModeUrl, StreamType = "Test", IsPrimary = true, IsActive = true }
            };
            await context.CameraStreams.AddRangeAsync(streams);
            await context.SaveChangesAsync();
            Console.WriteLine($"✅ Đã thêm {streams.Length} camera streams.");
        }

        // 3. Seed WeatherLogs (Nếu chưa có)
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

        // --- 4. SEED USER ADMIN (MỚI) ---
        // Kiểm tra xem đã có admin chưa, nếu chưa thì tạo
        if (!context.Users.Any(u => u.Role == AppConstants.UserRoles.Admin))
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
                Role = AppConstants.UserRoles.Admin, // Quyền cao nhất
                CreatedAt = DateTime.UtcNow
            };

            await context.Users.AddAsync(adminUser);
            await context.SaveChangesAsync();
            
            Console.WriteLine("✅ Đã tạo User: admin / admin123");
        }
    }

    /// <summary>
    /// Migration dữ liệu từ cấu trúc cũ sang mới (Chạy 1 lần)
    /// </summary>
    private static async Task MigrateOldData(AppDbContext context)
    {
        // 1. Tạo Ward mặc định nếu chưa có
        if (!await context.Wards.AnyAsync())
        {
            Console.WriteLine("🏘️ Tạo Ward mặc định...");
            context.Wards.Add(new Ward 
            { 
                WardId = "DEFAULT", 
                WardName = "Chưa xác định", 
                DistrictName = "Chưa xác định",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // 2. Gán Ward mặc định cho các Camera chưa có WardId
        var camerasWithoutWard = await context.Cameras
            .Where(c => c.WardId == null)
            .ToListAsync();

        if (camerasWithoutWard.Any())
        {
            Console.WriteLine($"🏘️ Gán Ward mặc định cho {camerasWithoutWard.Count} cameras...");
            foreach (var cam in camerasWithoutWard)
            {
                cam.WardId = "DEFAULT";
            }
            await context.SaveChangesAsync();
        }
    }
}
