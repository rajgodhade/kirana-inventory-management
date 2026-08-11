using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations;

[DbContext(typeof(KiranaDbContext))]
[Migration("20260812090000_AddSupplierCashPaymentsToRegister")]
public partial class AddSupplierCashPaymentsToRegister : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Z-report snapshot column only. Supplier cash payments for an OPEN register are derived
        // live from SupplierPayments, exactly as cash sales and udhaar repayments are; this column
        // freezes that figure at close so a historical Z report never re-computes.
        migrationBuilder.AddColumn<decimal>(
            name: "SupplierCashPayments",
            table: "CashRegisterSessions",
            type: "TEXT",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SupplierCashPayments", table: "CashRegisterSessions");
    }
}
