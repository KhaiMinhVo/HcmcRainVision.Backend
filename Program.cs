using HcmcRainVision.Backend.Data;
using HcmcRainVision.Backend.BackgroundJobs;
using HcmcRainVision.Backend.Services.Crawling;
using HcmcRainVision.Backend.Services.ImageProcessing;
using HcmcRainVision.Backend.Services.AI;
using HcmcRainVision.Backend.Services.Notification;
using HcmcRainVision.Backend.Hubs;
using HcmcRainVision.Backend;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ML;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;

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

// 4. Đăng ký Background Worker (Chạy ngầm)
builder.Services.AddHostedService<RainScanningWorker>();

// 5. Đăng ký AI Service với PredictionEnginePool (Thread-safe)
// Nếu có file RainAnalysisModel.zip, pool sẽ được sử dụng
// Nếu không, service sẽ fallback sang Mock mode
var modelPath = Path.Combine(builder.Environment.ContentRootPath, "RainAnalysisModel.zip");
if (File.Exists(modelPath))
{
    builder.Services.AddPredictionEnginePool<ModelInput, ModelOutput>()
        .FromFile(modelName: "RainModel", filePath: modelPath, watchForChanges: true);
}
builder.Services.AddSingleton<RainPredictionService>();

// 6. Đăng ký Email Service
builder.Services.AddTransient<IEmailService, EmailService>();

// 7. Đăng ký JWT Authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8
                .GetBytes(builder.Configuration.GetSection("JwtSettings:Key").Value!)),
            ValidateIssuer = false,
            ValidateAudience = false
        };
    });

// 8. Đăng ký Controllers
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

// Seed test data (tạm thời cho phép chạy ở mọi môi trường để khởi tạo dữ liệu)
// TODO: Sau khi có dữ liệu, nên thêm lại điều kiện IsDevelopment() để bảo mật
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await TestDataSeeder.SeedTestData(dbContext);
}

// Pipeline
// Luôn hiện Swagger để chấm bài (cả Production)
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("AllowReactApp");

// Cho phép truy cập file trong thư mục wwwroot
app.UseStaticFiles();

// Thêm Authentication và Authorization middleware (theo đúng thứ tự)
app.UseAuthentication(); // Xác thực: Bạn là ai?
app.UseAuthorization();  // Phân quyền: Bạn được làm gì?

app.MapControllers();

// Đăng ký SignalR Hub endpoint
app.MapHub<RainHub>("/rainHub");

app.Run();