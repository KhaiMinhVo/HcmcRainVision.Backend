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
        public DbSet<UserReport> UserReports { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<FavoriteCamera> FavoriteCameras { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // Kích hoạt PostGIS extension
            modelBuilder.HasPostgresExtension("postgis");
            
            // Đảm bảo Username và Email là duy nhất
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Username)
                .IsUnique();
                
            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();
            
            base.OnModelCreating(modelBuilder);
        }
    }
}
