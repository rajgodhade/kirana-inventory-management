using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUnitPackFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PurchasedPackQuantitySnapshot",
                table: "PurchaseItems",
                type: "TEXT",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchasedPackUnitSnapshot",
                table: "PurchaseItems",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PurchasePackSize",
                table: "Products",
                type: "TEXT",
                precision: 18,
                scale: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PurchasePackUnit",
                table: "Products",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitDisplayText",
                table: "Products",
                type: "TEXT",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PurchasedPackQuantitySnapshot",
                table: "PurchaseItems");

            migrationBuilder.DropColumn(
                name: "PurchasedPackUnitSnapshot",
                table: "PurchaseItems");

            migrationBuilder.DropColumn(
                name: "PurchasePackSize",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "PurchasePackUnit",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "UnitDisplayText",
                table: "Products");
        }
    }
}
