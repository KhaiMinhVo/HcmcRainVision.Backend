using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.BackgroundJobs;
using HcmcRainVision.Backend.Services.Crawling;
using HcmcRainVision.Backend.Services.ImageProcessing;
using HcmcRainVision.Backend.Services.AI;
using HcmcRainVision.Backend.Services.Notification;
using HcmcRainVision.Backend.Hubs;
using HcmcRainVision.Backend;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Load local configuration file if exists (for sensitive credentials)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// 1. Đăng ký Database (PostgreSQL)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())); // Quan trọng: Kích hoạt GIS

// 2. Đăng ký HttpClient Factory
builder.Services.AddHttpClient();

// 3. Đăng ký các Service (Dependency Injection)
builder.Services.AddSingleton<ICameraCrawler, CameraCrawler>();
builder.Services.AddSingleton<IImagePreProcessor, ImagePreProcessor>();
// builder.Services.AddSingleton<RainPredictionService>();

// 4. Đăng ký Background Worker (Chạy ngầm)
builder.Services.AddHostedService<RainScanningWorker>();

// 5. Đăng ký AI Service
builder.Services.AddSingleton<RainPredictionService>();

// 6. Đăng ký Email Service
builder.Services.AddTransient<IEmailService, EmailService>();

// 7. Đăng ký Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// 8. Đăng ký SignalR
builder.Services.AddSignalR();

// 9. Cấu hình CORS (Để React gọi được API + SignalR)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowReactApp",
        policy => policy.WithOrigins("http://localhost:5173") // Cổng mặc định của Vite React
                        .AllowAnyMethod()
                        .AllowAnyHeader()
                        .AllowCredentials()); // Bắt buộc cho SignalR
});

var app = builder.Build();

// Seed test data (chỉ chạy khi development và DB trống)
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await TestDataSeeder.SeedTestData(dbContext);
    }
}

// Pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors("AllowReactApp");

// Cho phép truy cập file trong thư mục wwwroot
app.UseStaticFiles();

app.UseAuthorization();
app.MapControllers();

// Đăng ký SignalR Hub endpoint
app.MapHub<RainHub>("/rainHub");

app.Run();