# 🚀 HCMC Rain Vision - Deploy Guide

## 📁 Files đã tạo

1. **[.env.example](.env.example)** - Template environment variables
2. **[DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)** - Hướng dẫn deploy đầy đủ
3. **[QUICK_DEPLOY.md](QUICK_DEPLOY.md)** - Hướng dẫn deploy nhanh
4. **[setup-local-env.ps1](setup-local-env.ps1)** - Script setup local (Windows)

## ⚡ Để deploy ngay lập tức

### Option 1: Nếu bạn muốn giữ credentials trong code (KHÔNG KHUYẾN NGHỊ)

```bash
git add .
git commit -m "docs: Add deployment guide"
git push origin main
```

Render sẽ tự động deploy nếu đã bật Auto-Deploy.

### Option 2: Deploy an toàn với Environment Variables (KHUYẾN NGHỊ)

👉 Đọc [QUICK_DEPLOY.md](QUICK_DEPLOY.md) - Thời gian: ~10 phút

Các bước:
1. Xóa credentials từ `appsettings.json`
2. Setup Environment Variables trên Render
3. Push code và deploy

## 🖥️ Để chạy local

### Windows (PowerShell):
```powershell
.\setup-local-env.ps1
```

### Manual:
```bash
# 1. Tạo file config local
cp appsettings.Local.json.example appsettings.Local.json

# 2. Chỉnh sửa appsettings.Local.json với credentials thật

# 3. Chạy migrations
dotnet ef database update

# 4. Chạy app
dotnet run
```

## 🔐 Bảo mật

⚠️ **QUAN TRỌNG:** File `appsettings.json` hiện đang chứa credentials thật!

Để bảo mật:
1. Xóa credentials từ `appsettings.json` (xem QUICK_DEPLOY.md)
2. Sử dụng `appsettings.Local.json` cho local development
3. Sử dụng Environment Variables cho production

## 📚 Tài liệu

- **Deploy production:** [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md)
- **Deploy nhanh:** [QUICK_DEPLOY.md](QUICK_DEPLOY.md)  
- **Cấu hình:** [CONFIGURATION.md](CONFIGURATION.md)
- **API:** [API_AUTHORIZATION.md](API_AUTHORIZATION.md)

## 🆘 Cần giúp?

- Xem logs trên Render Dashboard
- Kiểm tra [DEPLOYMENT_GUIDE.md](DEPLOYMENT_GUIDE.md) phần "Xử lý sự cố"
- Test local trước khi deploy: `dotnet run`

---

**Tạo bởi:** GitHub Copilot  
**Ngày:** 5/3/2026
