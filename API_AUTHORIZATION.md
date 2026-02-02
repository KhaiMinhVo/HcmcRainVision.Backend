# 🔐 API Authorization & Role Management

## Tổng quan phân quyền

Hệ thống có 3 levels phân quyền:
1. **Public** - Không cần authentication
2. **User** - Cần đăng nhập (JWT Token)
3. **Admin** - Cần đăng nhập với role Admin

---

## 📋 Danh sách API theo Role

### 🌍 PUBLIC APIs (Không cần đăng nhập)

#### Weather Data
| Method | Endpoint | Mô tả |
|--------|----------|-------|
| GET | `/api/weather/latest` | Dữ liệu mưa 30 phút gần nhất |
| GET | `/api/weather/heatmap` | Dữ liệu bản đồ nhiệt |
| POST | `/api/weather/check-route` | Kiểm tra route có đi qua vùng mưa |

#### Camera Information
| Method | Endpoint | Mô tả |
|--------|----------|-------|
| GET | `/api/camera` | Danh sách tất cả cameras |

#### Authentication
| Method | Endpoint | Mô tả |
|--------|----------|-------|
| POST | `/api/auth/register` | Đăng ký tài khoản mới |
| POST | `/api/auth/login` | Đăng nhập (nhận JWT token) |
| POST | `/api/auth/forgot-password` | Gửi email reset password |
| POST | `/api/auth/reset-password` | Reset password với token |

---

### 👤 USER APIs (Cần đăng nhập - [Authorize])

#### User Profile
| Method | Endpoint | Mô tả | Authorization |
|--------|----------|-------|---------------|
| GET | `/api/auth/me` | Xem thông tin cá nhân | `[Authorize]` |
| PUT | `/api/auth/me` | Cập nhật profile | `[Authorize]` |

#### Weather Reports
| Method | Endpoint | Mô tả | Authorization |
|--------|----------|-------|---------------|
| POST | `/api/weather/report` | Báo cáo AI sai | `[Authorize]` |

#### Favorites Management
| Method | Endpoint | Mô tả | Authorization |
|--------|----------|-------|---------------|
| GET | `/api/favorite` | Danh sách cameras yêu thích | `[Authorize]` |
| POST | `/api/favorite/{cameraId}` | Thêm camera yêu thích | `[Authorize]` |
| DELETE | `/api/favorite/{cameraId}` | Xóa camera yêu thích | `[Authorize]` |

---

### 👑 ADMIN APIs (Chỉ Admin - [Authorize(Roles = "Admin")])

#### System Statistics
| Method | Endpoint | Mô tả | Authorization |
|--------|----------|-------|---------------|
| GET | `/api/admin/stats` | Thống kê tổng quan hệ thống | `[Authorize(Roles = "Admin")]` |
| GET | `/api/admin/stats/rain-frequency` | Thống kê tần suất mưa theo giờ | `[Authorize(Roles = "Admin")]` |
| GET | `/api/admin/stats/failed-cameras` | Cameras không hoạt động | `[Authorize(Roles = "Admin")]` |
| GET | `/api/admin/stats/check-camera-health` | Health check real-time cameras | `[Authorize(Roles = "Admin")]` |

#### Data Management
| Method | Endpoint | Mô tả | Authorization |
|--------|----------|-------|---------------|
| GET | `/api/admin/audit-data` | User reports cần review | `[Authorize(Roles = "Admin")]` |

#### User Management
| Method | Endpoint | Mô tả | Authorization |
|--------|----------|-------|---------------|
| GET | `/api/admin/users` | Danh sách tất cả users | `[Authorize(Roles = "Admin")]` |
| PUT | `/api/admin/users/{id}/ban` | Khóa/mở khóa user | `[Authorize(Roles = "Admin")]` |

#### Camera Management
| Method | Endpoint | Mô tả | Authorization |
|--------|----------|-------|---------------|
| POST | `/api/camera` | Thêm camera mới | `[Authorize(Roles = "Admin")]` |
| PUT | `/api/camera/{id}` | Cập nhật thông tin camera | `[Authorize(Roles = "Admin")]` |
| DELETE | `/api/camera/{id}` | Xóa camera | `[Authorize(Roles = "Admin")]` |

---

## 🔑 Cách sử dụng Authorization

### 1. Public APIs
Gọi trực tiếp, không cần header:
```bash
GET https://api.hcmcrainvision.com/api/weather/latest
```

### 2. User APIs
Cần JWT token trong header:
```bash
GET https://api.hcmcrainvision.com/api/auth/me
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
```

### 3. Admin APIs
Cần JWT token của user có Role = "Admin":
```bash
POST https://api.hcmcrainvision.com/api/camera
Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...
Content-Type: application/json

{
  "id": "CAM_NEW_001",
  "name": "Camera mới",
  "sourceUrl": "http://...",
  "latitude": 10.7769,
  "longitude": 106.7009
}
```

---

## 🔒 Implementation Details

### Code Implementation

#### Public API (No attribute)
```csharp
[HttpGet("latest")]
public async Task<IActionResult> GetLatestWeather()
{
    // Anyone can access
}
```

#### User API (Authorize)
```csharp
[Authorize]
[HttpPost("report")]
public async Task<IActionResult> ReportIncorrectPrediction([FromBody] ReportDto input)
{
    // Only logged-in users
    var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
```

#### Admin API (Authorize with Role)
```csharp
[Authorize(Roles = "Admin")]
[HttpPost]
public async Task<IActionResult> AddCamera([FromBody] Camera camera)
{
    // Only admin users
}
```

---

## 📝 Tạo tài khoản Admin

### Cách 1: Qua TestDataSeeder (Đã có sẵn)
Khi chạy lần đầu, seeder tự động tạo:
- Username: `admin`
- Password: `admin123`
- Role: `Admin`

### Cách 2: Thủ công qua Database
```sql
INSERT INTO "Users" ("Username", "Email", "PasswordHash", "Role", "CreatedAt")
VALUES (
  'newadmin',
  'newadmin@example.com',
  '$2a$11$...', -- BCrypt hash of password
  'Admin',
  NOW()
);
```

### Cách 3: Promote user hiện tại
```sql
UPDATE "Users"
SET "Role" = 'Admin'
WHERE "Username" = 'existing_user';
```

---

## 🧪 Testing với Swagger

### 1. Đăng nhập để lấy token:
```
POST /api/auth/login
{
  "username": "admin",
  "password": "admin123"
}
```

Response:
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "username": "admin",
  "role": "Admin"
}
```

### 2. Click "Authorize" button trên Swagger UI
- Nhập: `Bearer <TOKEN>`
- Click "Authorize"

### 3. Test protected endpoints
Bây giờ có thể gọi các API có `🔒` (lock icon) trong Swagger

---

## ⚠️ Security Best Practices

### ✅ Đã implement:
- [x] Password hashing với BCrypt
- [x] JWT token expiration (configurable)
- [x] Role-based authorization
- [x] HTTPS only (trong production)
- [x] CORS policy với whitelist

### 🔒 Nên thêm (Advanced):
- [ ] Rate limiting per IP/User
- [ ] Refresh token mechanism
- [ ] Two-factor authentication (2FA)
- [ ] Account lockout sau n lần đăng nhập sai
- [ ] Audit logging cho admin actions
- [ ] API key authentication cho external services

---

## 📊 Role Matrix Summary

| API Category | Public | User | Admin |
|--------------|--------|------|-------|
| Weather data (read) | ✅ | ✅ | ✅ |
| Route checking | ✅ | ✅ | ✅ |
| Camera list (read) | ✅ | ✅ | ✅ |
| User registration/login | ✅ | ✅ | ✅ |
| Profile management | ❌ | ✅ | ✅ |
| Weather reporting | ❌ | ✅ | ✅ |
| Favorites | ❌ | ✅ | ✅ |
| System stats | ❌ | ❌ | ✅ |
| Camera management | ❌ | ❌ | ✅ |
| User management | ❌ | ❌ | ✅ |

---

## 🚀 Testing Checklist

- [ ] Public APIs accessible without token
- [ ] User APIs return 401 without token
- [ ] User APIs work with valid user token
- [ ] Admin APIs return 403 with user (non-admin) token
- [ ] Admin APIs work with admin token
- [ ] Invalid/expired tokens return 401
- [ ] Token includes correct claims (id, username, role)

---

**Cập nhật:** 2 tháng 2, 2026  
**Phiên bản:** 1.0
