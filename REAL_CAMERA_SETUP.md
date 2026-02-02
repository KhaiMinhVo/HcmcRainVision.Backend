# 📹 Hướng dẫn kết nối Camera thật từ TP.HCM

## Tổng quan

Hệ thống đã được cấu hình sẵn để làm việc với camera thật từ Cổng thông tin giao thông TP.HCM. File `CameraCrawler.cs` đã có:
- ✅ User-Agent giả lập trình duyệt
- ✅ Referer header đúng
- ✅ Retry mechanism (3 lần)
- ✅ Timeout 10 giây

## 🔍 Bước 1: Tìm URL Camera

### Cách thủ công (Recommended):

1. **Truy cập trang chính:**
   ```
   http://giaothong.hochiminhcity.gov.vn
   ```

2. **Chọn camera trên bản đồ:**
   - Click vào bất kỳ điểm camera nào (chấm xanh/đỏ trên map)
   - Popup hiển thị hình ảnh camera

3. **Lấy URL ảnh:**
   - **Cách 1:** Chuột phải vào ảnh → "Open image in new tab" → Copy URL từ address bar
   - **Cách 2:** Chuột phải → "Inspect" → Tab Network → Tìm request có dạng `ImageHandler.ashx` → Copy URL

4. **URL mẫu:**
   ```
   http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f707
   ```

   **Phần quan trọng:** `id=5896ddb359f14b001221f707` - Đây là ID duy nhất của mỗi camera

### Cách tự động (Advanced):

Nếu bạn muốn lấy danh sách tất cả cameras, có thể:
1. Inspect Network tab khi load trang
2. Tìm API endpoint trả về danh sách cameras (thường là JSON)
3. Parse JSON để lấy tất cả IDs

## 🔧 Bước 2: Cập nhật Database

### Option 1: Chỉnh sửa TestDataSeeder.cs (Khuyến nghị cho development)

File đã được chuẩn bị sẵn với placeholder URLs. Bạn chỉ cần:

```csharp
// Trong TestDataSeeder.cs
new Camera 
{ 
    Id = "CAM_Q1_001", 
    Name = "Ngã tư Lê Duẩn - Pasteur (Q1)", 
    // Thay ID này bằng ID thật bạn tìm được
    SourceUrl = "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=<ID_THẬT>",
    Latitude = 10.7797, 
    Longitude = 106.6990 
}
```

**Sau đó:**
```bash
# Xóa database hiện tại và seed lại
dotnet ef database drop
dotnet ef database update

# Hoặc chỉ update cameras trong DB hiện tại (không mất data)
# Dùng SQL hoặc Admin API
```

### Option 2: Sử dụng Admin API (Khuyến nghị cho production)

Nếu đã có database chạy production, dùng API để thêm/sửa camera:

**1. Đăng nhập Admin:**
```bash
POST /api/auth/login
{
  "username": "admin",
  "password": "admin123"
}
```

**2. Thêm camera mới:**
```bash
POST /api/camera
Authorization: Bearer <TOKEN>
{
  "id": "CAM_Q1_REAL_001",
  "name": "Ngã tư Lê Duẩn - Pasteur (Real)",
  "sourceUrl": "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=5896ddb359f14b001221f707",
  "latitude": 10.7797,
  "longitude": 106.6990
}
```

## 🧪 Bước 3: Test Camera

### Test thủ công qua Postman/curl:

```bash
curl "http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=YOUR_ID" \
  -H "Referer: http://giaothong.hochiminhcity.gov.vn/" \
  -H "User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36" \
  --output test_image.jpg
```

Nếu file `test_image.jpg` hiển thị được → URL hợp lệ ✅

### Test trong ứng dụng:

Sau khi cập nhật URL, chạy app và xem logs:

```bash
dotnet run
```

**Logs cần chú ý:**
```
✅ Đang tải ảnh từ: http://... (Lần thử: 1)
✅ Đã gửi Alert cho CAM_Q1_001
```

**Nếu gặp lỗi:**
```
❌ Bỏ cuộc sau 3 lần thử camera ...
⚠️ URL không trả về ảnh! Nhận được: text/html
```
→ Kiểm tra lại URL hoặc ID có đúng không

## ⚙️ Bước 4: Tối ưu cấu hình

### 4.1. Điều chỉnh tần suất quét

Mặc định: 5 phút/lần

**Để tăng tốc độ cập nhật (1-2 phút):**

File: `BackgroundJobs/RainScanningWorker.cs`

```csharp
// Dòng 191 - Thay đổi từ 5 phút xuống 2 phút
await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
```

**⚠️ Lưu ý:**
- Quét nhanh = tốn bandwidth hơn
- Có thể bị server camera block nếu quá nhiều request
- Khuyến nghị: 2-3 phút cho production

### 4.2. Cấu hình số lượng camera xử lý song song

File: `BackgroundJobs/RainScanningWorker.cs` (Dòng ~65)

```csharp
// Tăng từ 5 lên 10 để xử lý nhanh hơn (nếu server đủ mạnh)
var parallelOptions = new ParallelOptions { 
    MaxDegreeOfParallelism = 10, 
    CancellationToken = stoppingToken 
};
```

### 4.3. Tăng timeout cho camera chậm

File: `Services/Crawling/CameraCrawler.cs` (Dòng ~47)

```csharp
// Tăng từ 10s lên 15s nếu camera thường bị timeout
client.Timeout = TimeSpan.FromSeconds(15);
```

## 📊 Bước 5: Giám sát hoạt động

### Kiểm tra camera offline:

```bash
GET /api/admin/stats/failed-cameras
Authorization: Bearer <ADMIN_TOKEN>
```

Response sẽ liệt kê cameras không có dữ liệu trong 1h qua.

### Xem logs real-time:

```bash
# Trong PowerShell/Terminal khi chạy app
INFO: Đang tải ảnh từ: http://...
INFO: 📡 Đã gửi Alert cho CAM_Q1_001
INFO: 💾 Lưu ảnh uncertain (0.55) cho CAM_Q3_001
```

### Kiểm tra database:

```sql
-- Xem camera nào đang hoạt động
SELECT CameraId, MAX(Timestamp) as LastSeen
FROM WeatherLogs
GROUP BY CameraId
ORDER BY LastSeen DESC;

-- Số lượng detections trong 1h qua
SELECT COUNT(*) as TotalScans
FROM WeatherLogs
WHERE Timestamp > NOW() - INTERVAL '1 hour';
```

## 🐛 Troubleshooting

### Lỗi 403 Forbidden
**Nguyên nhân:** Server camera block do thiếu Referer header

**Giải pháp:** 
- Đã được fix trong `CameraCrawler.cs` (dòng 46)
- Đảm bảo Referer = `http://giaothong.hochiminhcity.gov.vn/`

### Lỗi 404 Not Found
**Nguyên nhân:** ID camera không tồn tại hoặc đã bị xóa

**Giải pháp:**
- Kiểm tra lại ID trên website chính thức
- Có thể camera đã bị remove khỏi hệ thống

### Camera trả về HTML thay vì ảnh
**Nguyên nhân:** Server trả về trang lỗi hoặc captcha

**Giải pháp:**
- Thử truy cập URL trên browser để xem nội dung
- Có thể cần thêm cookies/session handling

### Tất cả cameras đều TEST_MODE
**Nguyên nhân:** Chưa seed lại database sau khi sửa URLs

**Giải pháp:**
```bash
# Option 1: Drop và recreate (MẤT DATA)
dotnet ef database drop
dotnet ef database update

# Option 2: Chỉ update cameras qua SQL
UPDATE "Cameras"
SET "SourceUrl" = 'http://giaothong.hochiminhcity.gov.vn/render/ImageHandler.ashx?id=...'
WHERE "Id" = 'CAM_Q1_001';
```

## 🎯 Danh sách Camera đề xuất (TP.HCM)

Các camera quan trọng nên ưu tiên:

| Khu vực | Camera | Lý do |
|---------|--------|-------|
| Quận 1 | Ngã tư Lê Duẩn - Pasteur | Trung tâm thành phố |
| Quận 1 | Bến Thành - Lê Lợi | Khu du lịch |
| Quận 3 | CMT8 - 3 Tháng 2 | Giao thông cao điểm |
| Tân Bình | Sân bay Tân Sơn Nhất | Quan trọng logistics |
| Quận 7 | Phú Mỹ Hưng | Khu dân cư đông |
| Bình Thạnh | Cầu Bình Triệu | Cửa ngõ phía Đông |

## 📝 Checklist triển khai

- [ ] Tìm được ít nhất 5 URL camera thật
- [ ] Cập nhật TestDataSeeder.cs với URLs mới
- [ ] Test từng URL bằng curl/Postman
- [ ] Seed lại database (hoặc update qua API)
- [ ] Chạy app và kiểm tra logs
- [ ] Kiểm tra WeatherLogs có data mới không
- [ ] Xem Admin dashboard để monitor
- [ ] Điều chỉnh tần suất quét nếu cần
- [ ] Deploy lên production (Render)
- [ ] Thêm biến môi trường nếu cần

## 🚀 Production Deployment

Khi deploy lên Render:

1. **Commit code mới:**
   ```bash
   git add .
   git commit -m "feat: Add real camera URLs from HCMC traffic system"
   git push
   ```

2. **Render tự động deploy** (nếu đã setup auto-deploy)

3. **Sau khi deploy xong, seed lại database:**
   - Option A: Trong `Program.cs`, seeding tự động chạy
   - Option B: Dùng Admin API để thêm cameras thủ công

4. **Monitor logs trên Render:**
   - Vào Render Dashboard → Logs tab
   - Xem có lỗi gì không

## 💡 Tips & Best Practices

1. **Luôn giữ 1 camera TEST_MODE** để làm fallback khi cameras thật offline
2. **Không quét quá nhanh** (< 1 phút) để tránh bị block IP
3. **Rotate User-Agent** (đã implement sẵn) để tránh bị phát hiện là bot
4. **Log đầy đủ** để dễ debug khi có vấn đề
5. **Backup database** trước khi drop/recreate
6. **Test trên local** trước khi deploy production

---

**Cần hỗ trợ thêm?** Kiểm tra logs hoặc liên hệ team DevOps!
