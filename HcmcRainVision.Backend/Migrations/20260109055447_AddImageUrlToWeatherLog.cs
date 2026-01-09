using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HcmcRainVision.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddImageUrlToWeatherLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "WeatherLogs",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "WeatherLogs");
        }
    }
}
