# 🚀 Hướng dẫn các tính năng mới

## Tổng quan các nâng cấp

Hệ thống đã được nâng cấp với các tính năng sau:

### 1. 🗺️ Rain Heatmap API
**Endpoint:** `GET /api/weather/heatmap`

Trả về dữ liệu bản đồ nhiệt (heatmap) để hiển thị cường độ mưa trên bản đồ.

**Response:**
```json
[
  {
    "lat": 10.7721,
    "lng": 106.6983,
    "intensity": 0.87
  }
]
```

**Cách sử dụng với Frontend:**
- Sử dụng Google Maps Heatmap Layer hoặc Leaflet.heat
- `intensity` (0-1) dựa trên độ tin cậy AI → màu sắc từ vàng đến đỏ

### 2. 📊 Admin Statistics APIs

#### 2.1. Thống kê tần suất mưa theo giờ
**Endpoint:** `GET /api/admin/stats/rain-frequency`

Thống kê số lượng sự kiện mưa trong 7 ngày qua, nhóm theo giờ.

**Response:**
```json
[
  { "hour": 0, "count": 15 },
  { "hour": 1, "count": 8 },
  ...
  { "hour": 23, "count": 12 }
]
```

#### 2.2. Danh sách camera offline
**Endpoint:** `GET /api/admin/stats/failed-cameras`

Liệt kê các camera không có dữ liệu mới trong 1 giờ qua.

**Response:**
```json
{
  "totalFailed": 2,
  "cameras": [
    {
      "id": "CAM_Q1_001",
      "name": "Ngã tư Lê Duẩn - Pasteur",
      "sourceUrl": "...",
      "latitude": 10.7797,
      "longitude": 106.6990,
      "status": "Offline - Không có dữ liệu mới"
    }
  ]
}
```

### 3. ☁️ Cloudinary Image Storage

**Service:** `ICloudStorageService`

Thay thế lưu trữ ảnh local bằng Cloudinary để:
- Tránh đầy ổ cứng server
- Có CDN tự động
- Quản lý ảnh chuyên nghiệp

**Cấu hình trong `appsettings.Local.json`:**
```json
{
  "CloudinarySettings": {
    "CloudName": "your_cloud_name",
    "ApiKey": "your_api_key",
    "ApiSecret": "your_api_secret"
  }
}
```

**Cách lấy credentials:**
1. Đăng ký tài khoản miễn phí tại: https://cloudinary.com/
2. Vào Dashboard → Copy Cloud name, API Key, API Secret
3. Paste vào `appsettings.Local.json`

**Note:** Nếu không cấu hình, hệ thống tự động fallback về lưu local.

### 4. 🔔 Firebase Push Notification

**Service:** `IFirebasePushService`

Gửi thông báo push đến điện thoại người dùng khi phát hiện mưa.

**Cấu hình trong `appsettings.Local.json`:**
```json
{
  "FirebaseSettings": {
    "ServiceAccountPath": "path/to/firebase-service-account.json"
  }
}
```

**Các bước setup Firebase:**
1. Tạo project Firebase tại: https://console.firebase.google.com/
2. Vào Project Settings → Service Accounts
3. Click "Generate new private key" → Lưu file JSON
4. Đặt file vào thư mục project và cập nhật path trong config

**API Methods:**
- `SendRainAlertAsync()` - Gửi cảnh báo mưa đến topic "rain_alerts"
- `SendToDeviceAsync()` - Gửi notification đến device token cụ thể

**Note:** Nếu không cấu hình, tính năng push notification sẽ bị vô hiệu hóa.

### 5. 📍 Mở rộng danh sách Camera

Đã thêm **8 camera** phủ các quận trọng điểm TP.HCM:

| ID | Tên Camera | Vị trí |
|---|---|---|
| CAM_Q1_001 | Ngã tư Lê Duẩn - Pasteur | Quận 1 |
| CAM_Q1_002 | Vòng xoay Quách Thị Trang | Quận 1 |
| CAM_Q3_001 | Ngã tư CMT8 - CMTT8 | Quận 3 |
| CAM_Q5_001 | Chợ An Đông | Quận 5 |
| CAM_Q7_001 | Phú Mỹ Hưng | Quận 7 |
| CAM_BINHTAN_001 | Cầu Bình Triệu | Bình Tân |
| CAM_TAN_BINH_001 | Sân bay Tân Sơn Nhất | Tân Bình |
| CAM_TEST_01 | Camera Test Mode | Bến Thành |

**Note:** Hiện tại đang dùng `TEST_MODE`. Để kết nối camera thật:
- Thay `SourceUrl` bằng URL camera từ hệ thống giao thông TPHCM
- Hoặc sử dụng API của nhà cung cấp camera

### 6. 🔐 Database Migration

Đã tạo entity mới: `UserNotificationSetting`

**Migration:** `AddUserNotificationSettings`

**Chạy migration:**
```bash
dotnet ef database update
```

**Cấu trúc bảng:**
- `Id` - Primary key
- `UserId` - Foreign key đến Users
- `DeviceToken` - FCM token của thiết bị
- `InterestedDistricts` - Danh sách quận quan tâm (string)
- `IsEnabled` - Bật/tắt nhận thông báo
- `CreatedAt` - Thời gian đăng ký

## 🛠️ Các bước triển khai

### Bước 1: Cập nhật database
```bash
cd d:\HcmcRainVision\backend
dotnet ef database update
```

### Bước 2: Cấu hình Cloudinary (Tùy chọn)
1. Tạo tài khoản Cloudinary
2. Copy credentials vào `appsettings.Local.json`
3. Restart ứng dụng

### Bước 3: Cấu hình Firebase (Tùy chọn)
1. Tạo Firebase project
2. Download service account JSON
3. Cập nhật path trong `appsettings.Local.json`
4. Restart ứng dụng

### Bước 4: Test các API mới
- Sử dụng Swagger UI: `http://localhost:5000/swagger`
- Hoặc Postman để test endpoints

## 📝 Lưu ý bảo mật

### ⚠️ QUAN TRỌNG: Không commit thông tin nhạy cảm!

**File cần bảo mật:**
- `appsettings.Local.json` → Đã được thêm vào `.gitignore`
- Firebase service account JSON
- Cloudinary credentials

**Trên Production (Render):**
1. Vào Dashboard Render
2. Chọn service của bạn
3. Vào tab "Environment"
4. Thêm các biến:
   - `CloudinarySettings__CloudName`
   - `CloudinarySettings__ApiKey`
   - `CloudinarySettings__ApiSecret`
   - `FirebaseSettings__ServiceAccountPath`

## 🎯 Roadmap tiếp theo

### Tính năng có thể mở rộng:
1. **Smart Routing:** Gợi ý lộ trình tránh mưa
2. **User preferences:** Lưu khu vực yêu thích, gửi alert có chọn lọc
3. **Historical data:** Phân tích xu hướng mưa theo mùa
4. **AI training:** Thu thập feedback từ user để cải thiện model
5. **Real camera integration:** Kết nối với camera thật từ TPHCM

## 🐛 Troubleshooting

### Build lỗi?
```bash
dotnet restore
dotnet build
```

### Migration lỗi?
```bash
dotnet ef migrations remove
dotnet ef migrations add AddUserNotificationSettings
dotnet ef database update
```

### Firebase/Cloudinary không hoạt động?
- Kiểm tra logs: Service sẽ in ra `⚠️` warning nếu chưa cấu hình
- Hệ thống vẫn chạy bình thường, chỉ tính năng đó bị vô hiệu hóa

## 📞 Support

Nếu gặp vấn đề, kiểm tra:
1. Console logs khi chạy ứng dụng
2. Database có migration mới nhất chưa
3. Configuration files có đúng format JSON không
