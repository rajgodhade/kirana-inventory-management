using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase14DReplenishment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PreferredSupplierId",
                table: "Products",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ReplenishmentEnabled",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Products_PreferredSupplierId",
                table: "Products",
                column: "PreferredSupplierId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_ReplenishmentEnabled",
                table: "Products",
                column: "ReplenishmentEnabled");

            migrationBuilder.AddForeignKey(
                name: "FK_Products_Suppliers_PreferredSupplierId",
                table: "Products",
                column: "PreferredSupplierId",
                principalTable: "Suppliers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Products_Suppliers_PreferredSupplierId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_PreferredSupplierId",
                table: "Products");

            migrationBuilder.DropIndex(
                name: "IX_Products_ReplenishmentEnabled",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PreferredSupplierId",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "ReplenishmentEnabled",
                table: "Products");
        }
    }
}
