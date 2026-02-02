# HCMC Rain Vision - Configuration Setup

## ⚠️ Security Notice

Sensitive credentials have been removed from `appsettings.json` files for security.

## 🔧 Local Development Setup

### Option 1: Using Local Configuration File (Recommended)

1. Copy the example file:
   ```bash
   cp appsettings.Local.json.example appsettings.Local.json
   ```

2. Edit `appsettings.Local.json` with your actual credentials:
   - Database password
   - Email address and app password

3. Update `Program.cs` to load this file (if not already configured):
   ```csharp
   builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
   ```

### Option 2: Using User Secrets

1. Initialize user secrets:
   ```bash
   dotnet user-secrets init
   ```

2. Set your credentials:
   ```bash
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=hcmc_rain_vision_dev;Username=postgres;Password=YOUR_PASSWORD"
   dotnet user-secrets set "EmailSettings:SenderEmail" "your-email@example.com"
   dotnet user-secrets set "EmailSettings:Password" "your-app-password"
   ```

### Option 3: Using Environment Variables

Set environment variables before running:

**Windows (PowerShell):**
```powershell
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=hcmc_rain_vision_dev;Username=postgres;Password=YOUR_PASSWORD"
$env:EmailSettings__SenderEmail="your-email@example.com"
$env:EmailSettings__Password="your-app-password"
```

**Linux/Mac:**
```bash
export ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=hcmc_rain_vision_dev;Username=postgres;Password=YOUR_PASSWORD"
export EmailSettings__SenderEmail="your-email@example.com"
export EmailSettings__Password="your-app-password"
```

## 📧 Gmail App Password Setup

To use Gmail for sending alerts:

1. Enable 2-Factor Authentication on your Google account
2. Go to: https://myaccount.google.com/apppasswords
3. Create a new App Password for "Mail"
4. Use the generated 16-character password (format: `xxxx xxxx xxxx xxxx`)

## 🔒 Production Deployment

For production, use:
- **Azure Key Vault** for Azure deployments
- **AWS Secrets Manager** for AWS deployments
- **Environment Variables** set securely in your hosting platform

Never commit real passwords to version control!
