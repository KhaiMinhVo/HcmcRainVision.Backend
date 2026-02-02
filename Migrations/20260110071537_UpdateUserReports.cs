using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HcmcRainVision.Backend.Migrations
{
    /// <inheritdoc />
    public partial class UpdateUserReports : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "UserReports");

            migrationBuilder.RenameColumn(
                name: "UserIdentifier",
                table: "UserReports",
                newName: "Note");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Note",
                table: "UserReports",
                newName: "UserIdentifier");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "UserReports",
                type: "text",
                nullable: true);
        }
    }
}
