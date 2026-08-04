using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceFooterText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceFooterText",
                table: "Stores",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceFooterText",
                table: "Stores");
        }
    }
}
