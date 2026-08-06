using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPinAuthorizationSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Default true (not the CLR default) is deliberate: on an existing install, this
            // ALTER TABLE runs against the store's live AppSettings row, and a false default would
            // silently switch off PIN authorization on upgrade instead of preserving current
            // behavior until the Owner explicitly opts out in Settings.
            migrationBuilder.AddColumn<bool>(
                name: "RequirePinForLargeDiscount",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequirePinForPriceOverride",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequirePinForReprint",
                table: "AppSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RequirePinForLargeDiscount",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RequirePinForPriceOverride",
                table: "AppSettings");

            migrationBuilder.DropColumn(
                name: "RequirePinForReprint",
                table: "AppSettings");
        }
    }
}
