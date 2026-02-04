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
using Microsoft.AspNetCore.Mvc;
using System.Text;

// ===================================================================
// HCMC Rain Vision Backend API
// 
// QUAN TRỌNG - CHÍNH SÁCH SỬ DỤNG DỮ LIỆU:
// - Dữ liệu camera từ: http://giaothong.hochiminhcity.gov.vn
// - Mục đích: Nghiên cứu, học tập, phi lợi nhuận
// - Frontend PHẢI hiển thị: "Dữ liệu camera từ Cổng thông tin giao thông TP.HCM"
// - Xem thêm: POLICY.md
// ===================================================================

var builder = WebApplication.CreateBuilder(args);

// Load local configuration file if exists (for sensitive credentials)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// 1. Đăng ký Database (PostgreSQL)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString, o => o.UseNetTopologySuite())); // Quan trọng: Kích hoạt GIS

// 2. Đăng ký HttpClient Factory với Polly Resilience
builder.Services.AddHttpClient("CameraClient", client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.Referrer = new Uri("http://giaothong.hochiminhcity.gov.vn/");
})
.AddStandardResilienceHandler(); // Tự động retry, circuit breaker, timeout chuẩn Microsoft

// 2.1. Đăng ký HttpClient mặc định cho các service khác
builder.Services.AddHttpClient();

// 3. Đăng ký các Service (Dependency Injection)
builder.Services.AddSingleton<ICameraCrawler, CameraCrawler>();
builder.Services.AddSingleton<IImagePreProcessor, ImagePreProcessor>();
builder.Services.AddSingleton<ICloudStorageService, CloudStorageService>();

// 4. Đăng ký Background Worker (Chạy ngầm)
builder.Services.AddHostedService<RainScanningWorker>();

// 5. Đăng ký AI Service với Strategy Pattern (Interface-based)
var modelPath = Path.Combine(builder.Environment.ContentRootPath, "RainAnalysisModel.zip");
if (File.Exists(modelPath))
{
    // Có AI Model: Dùng ML.NET
    builder.Services.AddPredictionEnginePool<ModelInput, ModelOutput>()
        .FromFile(modelName: "RainModel", filePath: modelPath, watchForChanges: true);
    builder.Services.AddSingleton<IRainPredictionService, MlRainPredictionService>();
}
else
{
    // Không có Model: Dùng Mock Service cho development
    builder.Services.AddSingleton<IRainPredictionService, MockRainPredictionService>();
}

// 6. Đăng ký Email Service
builder.Services.AddTransient<IEmailService, EmailService>();

// 6.1. Đăng ký Firebase Push Notification Service
builder.Services.AddSingleton<IFirebasePushService, FirebasePushService>();

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

// 8. Đăng ký Controllers với JSON options và Model Validation
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Tùy chỉnh response khi ModelState invalid để hiển thị lỗi rõ ràng
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(e => e.Value?.Errors.Count > 0)
                .Select(e => new
                {
                    field = e.Key,
                    errors = e.Value!.Errors.Select(x => x.ErrorMessage).ToArray()
                })
                .ToList();

            return new BadRequestObjectResult(new
            {
                message = "Dữ liệu không hợp lệ. Vui lòng kiểm tra lại.",
                errors = errors
            });
        };
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = Microsoft.OpenApi.Models.SecuritySchemeType.Http,
        Scheme = "Bearer",
        BearerFormat = "JWT",
        In = Microsoft.OpenApi.Models.ParameterLocation.Header,
        Description = "Nhập 'Bearer' [space] rồi dán JWT token vào đây. Ví dụ: 'Bearer eyJhbG...'",
    });

    options.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
    {
        {
            new Microsoft.OpenApi.Models.OpenApiSecurityScheme
            {
                Reference = new Microsoft.OpenApi.Models.OpenApiReference
                {
                    Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

// 8. Đăng ký SignalR
builder.Services.AddSignalR();

// 8.1. Đăng ký Health Checks
builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString!); // Check kết nối DB

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

// Đăng ký Health Check endpoint
app.MapHealthChecks("/health");

app.Run();