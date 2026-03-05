# 🚀 Deployment Guide - HCMC Rain Vision Backend

## 📋 Yêu cầu trước khi deploy

- [ ] Tài khoản Render.com
- [ ] PostgreSQL database trên Render
- [ ] Repository GitHub
- [ ] Gmail App Password (cho email notifications)
- [ ] Cloudinary account (cho image storage)

## 🔐 Bước 1: Chuẩn bị Environment Variables

### Trên Render Dashboard

1. Vào **Render Dashboard** → Chọn service của bạn
2. Vào tab **Environment**
3. Thêm các biến sau:

#### Database Connection
```
ConnectionStrings__DefaultConnection
Host=dpg-xxx.singapore-postgres.render.com;Port=5432;Database=hcmc_rain_vision;Username=admin;Password=YOUR_DB_PASSWORD;Ssl Mode=Require;Trust Server Certificate=true
```

#### Email Settings
```
EmailSettings__SmtpServer=smtp.gmail.com
EmailSettings__Port=587
EmailSettings__SenderName=HCMC Rain Vision Alert
EmailSettings__SenderEmail=your-email@gmail.com
EmailSettings__Password=your-16-char-app-password
```

#### JWT Settings
```
JwtSettings__Key=generate-a-secure-random-key-at-least-32-characters-long
JwtSettings__Issuer=HcmcRainVisionBackend
JwtSettings__Audience=HcmcRainVisionClient
```

#### Cloudinary Settings
```
CloudinarySettings__CloudName=your-cloud-name
CloudinarySettings__ApiKey=your-api-key
CloudinarySettings__ApiSecret=your-api-secret
```

#### Optional Settings
```
FirebaseSettings__ServiceAccountPath=
ASPNETCORE_ENVIRONMENT=Production
```

## 🐳 Bước 2: Deploy với Docker trên Render

### Option A: Auto Deploy (Khuyến nghị)

1. **Kết nối GitHub repository:**
   - Trên Render Dashboard → New → Web Service
   - Connect repository: `HcmcRainVision`
   - Branch: `main` hoặc `master`

2. **Cấu hình service:**
   ```
   Name: hcmc-rain-vision-backend
   Environment: Docker
   Region: Singapore (gần database)
   Instance Type: Free hoặc Starter ($7/month)
   ```

3. **Dockerfile detection:**
   - Render tự động phát hiện `Dockerfile`
   - Build command: (để trống)
   - Start command: (để trống, dùng ENTRYPOINT trong Dockerfile)

4. **Auto Deploy:**
   - Bật "Auto-Deploy" → Mỗi lần push code sẽ tự động deploy

### Option B: Manual Deploy

```bash
# 1. Commit code mới
git add .
git commit -m "chore: Prepare for deployment"
git push origin main

# 2. Trigger manual deployment trên Render Dashboard
# Click "Manual Deploy" → Deploy latest commit
```

## 🗄️ Bước 3: Setup Database

### Tạo PostgreSQL Database trên Render

1. **New → PostgreSQL**
   ```
   Name: hcmc-rain-vision-db
   Database: hcmc_rain_vision
   User: admin
   Region: Singapore
   PostgreSQL Version: 15
   ```

2. **Lấy connection string:**
   - Vào database dashboard
   - Copy **External Database URL**
   - Format: `postgresql://user:pass@host:5432/dbname`

3. **Convert sang format Npgsql:**
   ```
   Host=dpg-xxx.singapore-postgres.render.com;
   Port=5432;
   Database=hcmc_rain_vision;
   Username=admin;
   Password=xxx;
   Ssl Mode=Require;
   Trust Server Certificate=true
   ```

### Chạy Migrations

Migrations tự động chạy khi app khởi động (nếu có trong `Program.cs`).

Hoặc chạy thủ công qua Render Shell:
```bash
dotnet ef database update
```

## 🔍 Bước 4: Kiểm tra Deploy

### 1. Xem Logs
- Render Dashboard → Logs tab
- Kiểm tra:
  ```
  ✓ Application started
  ✓ Listening on http://+:8080
  ✓ Database connection successful
  ```

### 2. Test API Endpoints

```bash
# Health check
curl https://your-app.onrender.com/

# Auth endpoint
curl https://your-app.onrender.com/api/Auth/login -X POST \
  -H "Content-Type: application/json" \
  -d '{"username":"admin","password":"admin123"}'

# Admin endpoint
curl https://your-app.onrender.com/api/Admin/users \
  -H "Authorization: Bearer YOUR_JWT_TOKEN"
```

### 3. Seed Database (nếu cần)

Database seeding tự động chạy khi app khởi động (trong `Program.cs`).

Kiểm tra logs để xác nhận:
```
✓ Seeded XX cameras
✓ Seeded XX wards
✓ Created admin user
```

## ⚠️ Xử lý sự cố thường gặp

### 1. Database Connection Failed
- Kiểm tra connection string có đúng format không
- Đảm bảo `Ssl Mode=Require` và `Trust Server Certificate=true`
- Kiểm tra database có online không

### 2. Email Sending Failed
- Xác nhận Gmail App Password đúng (16 ký tự, có dấu cách)
- Bật 2FA trên Google Account
- Tạo App Password tại: https://myaccount.google.com/apppasswords

### 3. Cloudinary Upload Failed
- Kiểm tra CloudName, ApiKey, ApiSecret
- Xác nhận account Cloudinary còn quota

### 4. JWT Validation Failed
- Đảm bảo `JwtSettings__Key` đủ dài (32+ ký tự)
- Key phải giống nhau giữa các instance

### 5. Build Failed
```
Error: Unable to restore packages
```
**Giải pháp:** Kiểm tra `.csproj` file, đảm bảo tất cả packages tồn tại

## 🔄 Bước 5: Update và Redeploy

### Update Code
```bash
# 1. Thay đổi code
# 2. Commit
git add .
git commit -m "feat: Add new feature"

# 3. Push (auto deploy nếu đã bật)
git push origin main
```

### Rollback nếu lỗi
Trên Render Dashboard:
1. Vào **Events** tab
2. Chọn commit trước đó
3. Click **Redeploy**

## 🔒 Security Checklist

- [ ] Xóa tất cả credentials từ `appsettings.json`
- [ ] Sử dụng Environment Variables cho mọi secrets
- [ ] Bật HTTPS only (Render tự động)
- [ ] Thêm `.env.example` vào repo, `.env` vào `.gitignore`
- [ ] Rotate JWT secret key định kỳ
- [ ] Backup database thường xuyên
- [ ] Monitor logs để phát hiện lỗi sớm

## 📊 Monitoring

### Render Dashboard
- CPU/Memory usage
- Response time
- Error rate
- Logs realtime

### Application Insights (nếu cần)
Thêm Application Insights SDK để monitor chi tiết:
```bash
dotnet add package Microsoft.ApplicationInsights.AspNetCore
```

## 💰 Chi phí ước tính (Render)

| Tier | Price | Resources |
|------|-------|-----------|
| Free | $0 | 512MB RAM, Sleep sau 15 phút inactive |
| Starter | $7/month | 512MB RAM, Always on |
| Standard | $25/month | 2GB RAM, Better performance |

**Database:**
| Tier | Price | Storage |
|------|-------|---------|
| Free | $0 | 90 ngày retention, 1GB storage |
| Starter | $7/month | 1 năm retention, 10GB storage |

## 📞 Support

Nếu gặp vấn đề:
1. Kiểm tra Render Logs
2. Xem documentation: [CONFIGURATION.md](CONFIGURATION.md)
3. Tham khảo: [REAL_CAMERA_SETUP.md](REAL_CAMERA_SETUP.md)

---

**Last Updated:** March 2026
