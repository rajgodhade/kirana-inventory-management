using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPriceOverrideAuthorization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PriceOverrideAuthorizedByUserId",
                table: "Sales",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_PriceOverrideAuthorizedByUserId",
                table: "Sales",
                column: "PriceOverrideAuthorizedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_Sales_Users_PriceOverrideAuthorizedByUserId",
                table: "Sales",
                column: "PriceOverrideAuthorizedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Sales_Users_PriceOverrideAuthorizedByUserId",
                table: "Sales");

            migrationBuilder.DropIndex(
                name: "IX_Sales_PriceOverrideAuthorizedByUserId",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PriceOverrideAuthorizedByUserId",
                table: "Sales");
        }
    }
}
