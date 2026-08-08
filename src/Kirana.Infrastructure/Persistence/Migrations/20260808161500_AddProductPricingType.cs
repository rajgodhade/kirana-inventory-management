using Kirana.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations;

[DbContext(typeof(KiranaDbContext))]
[Migration("20260808161500_AddProductPricingType")]
public sealed class AddProductPricingType : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PricingType",
            table: "Products",
            type: "TEXT",
            maxLength: 20,
            nullable: false,
            defaultValue: "Inclusive");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "PricingType",
            table: "Products");
    }
}
