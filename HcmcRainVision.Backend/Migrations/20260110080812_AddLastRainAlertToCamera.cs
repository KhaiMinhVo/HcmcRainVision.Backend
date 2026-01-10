using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HcmcRainVision.Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddLastRainAlertToCamera : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastRainAlertSent",
                table: "Cameras",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRainAlertSent",
                table: "Cameras");
        }
    }
}
