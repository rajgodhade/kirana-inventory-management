using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeMode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Light is the product default, so existing installations adopt it explicitly rather
            // than storing an empty string that only happens to parse back to Light by accident.
            migrationBuilder.AddColumn<string>(
                name: "ThemeMode",
                table: "AppSettings",
                type: "TEXT",
                nullable: false,
                defaultValue: "Light");

            migrationBuilder.Sql(
                "UPDATE AppSettings SET ThemeMode = 'Light' WHERE ThemeMode IS NULL OR ThemeMode = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThemeMode",
                table: "AppSettings");
        }
    }
}
