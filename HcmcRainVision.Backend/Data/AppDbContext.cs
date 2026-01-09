using Microsoft.EntityFrameworkCore;
using HcmcRainVision.Backend.Models.Entities;

namespace HcmcRainVision.Backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // Khai báo bảng
        public DbSet<WeatherLog> WeatherLogs { get; set; }
        public DbSet<Camera> Cameras { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Kích hoạt PostGIS extension
            modelBuilder.HasPostgresExtension("postgis");
            
            base.OnModelCreating(modelBuilder);
        }
    }
}
