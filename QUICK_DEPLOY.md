# ⚡ Quick Deploy Checklist

## 🚨 CẢNH BÁO BẢO MẬT

**File `appsettings.json` hiện tại đang chứa thông tin nhạy cảm được commit lên Git!**

❌ **KHÔNG AN TOÀN:**
- Password database
- Gmail app password  
- JWT secret key
- Cloudinary API keys

## ✅ Bước 1: Xóa thông tin nhạy cảm (QUAN TRỌNG!)

Thay thế nội dung trong [appsettings.json](appsettings.json) bằng:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "SET_VIA_ENVIRONMENT_VARIABLE"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*",
  
  "EmailSettings": {
    "SmtpServer": "smtp.gmail.com",
    "Port": 587,
    "SenderName": "HCMC Rain Vision Alert",
    "SenderEmail": "SET_VIA_ENVIRONMENT_VARIABLE",
    "Password": "SET_VIA_ENVIRONMENT_VARIABLE"
  },

  "JwtSettings": {
    "Key": "SET_VIA_ENVIRONMENT_VARIABLE",
    "Issuer": "HcmcRainVisionBackend",
    "Audience": "HcmcRainVisionClient"
  },

  "CloudinarySettings": {
    "CloudName": "SET_VIA_ENVIRONMENT_VARIABLE",
    "ApiKey": "SET_VIA_ENVIRONMENT_VARIABLE",
    "ApiSecret": "SET_VIA_ENVIRONMENT_VARIABLE"
  },

  "FirebaseSettings": {
    "ServiceAccountPath": ""
  }
}
```

## ✅ Bước 2: Tạo file Local cho development

```bash
cp .env.example .env
# Sau đó điền thông tin thật vào .env
```

Hoặc tạo `appsettings.Local.json` với thông tin thật:
```bash
cp appsettings.Local.json.example appsettings.Local.json
# Chỉnh sửa appsettings.Local.json với credentials thật
```

## ✅ Bước 3: Setup Environment Variables trên Render

Vào **Render Dashboard** → Service của bạn → **Environment** tab

Copy-paste các biến này (thay YOUR_XXX bằng giá trị thật):

```bash
# Database
ConnectionStrings__DefaultConnection=Host=YOUR_DB_HOST.render.com;Port=5432;Database=hcmc_rain_vision;Username=admin;Password=YOUR_DB_PASSWORD;Ssl Mode=Require;Trust Server Certificate=true

# Email
EmailSettings__SmtpServer=smtp.gmail.com
EmailSettings__Port=587
EmailSettings__SenderName=HCMC Rain Vision Alert
EmailSettings__SenderEmail=your-email@gmail.com
EmailSettings__Password=xxxx-xxxx-xxxx-xxxx

# JWT
JwtSettings__Key=YOUR_VERY_LONG_SECRET_KEY_AT_LEAST_32_CHARACTERS
JwtSettings__Issuer=HcmcRainVisionBackend
JwtSettings__Audience=HcmcRainVisionClient

# Cloudinary
CloudinarySettings__CloudName=your-cloud-name
CloudinarySettings__ApiKey=your-api-key
CloudinarySettings__ApiSecret=your-api-secret

# ASP.NET
ASPNETCORE_ENVIRONMENT=Production
```

## ✅ Bước 4: Commit và Push

```bash
git add .
git commit -m "security: Remove sensitive data from appsettings.json"
git push origin main
```

Render sẽ tự động deploy (nếu đã bật Auto-Deploy).

## ✅ Bước 5: Verify Deploy

1. **Xem Logs** trên Render Dashboard
2. **Test API:**
   ```bash
   curl https://your-app.onrender.com/api/Auth/login
   ```

## 🆘 Nếu deployed nhưng bị lỗi

### Lỗi: "Connection string not found"
→ Kiểm tra environment variable `ConnectionStrings__DefaultConnection` đã đúng chưa

### Lỗi: "Email sending failed"
→ Kiểm tra Gmail App Password (16 ký tự, có dấu cách)

### Lỗi: "JWT validation failed"
→ Đảm bảo `JwtSettings__Key` đủ dài (32+ ký tự)

## 📚 Chi tiết đầy đủ

Xem [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md) để biết thêm chi tiết.

---

⏱️ **Thời gian ước tính:** 10-15 phút
