using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations;

[DbContext(typeof(KiranaDbContext))]
[Migration("20260816120000_AddCashExpensesToRegister")]
public partial class AddCashExpensesToRegister : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Z-report snapshot column only, exactly like SupplierCashPayments before it. Cash expenses
        // for an OPEN register are derived live from the Expenses table — the same treatment cash
        // sales, cash refunds, udhaar repayments and supplier cash payments already get — and this
        // column freezes that figure at close so a historical Z report never re-computes.
        //
        // Deliberately NOT backfilled. Every pre-existing session gets 0, which reads as "the
        // cash-expense concept was not part of this snapshot", not as "no cash was spent". A
        // backfill would retro-subtract expenses from ExpectedCash and therefore change Variance on
        // sessions a human already counted, recorded and signed off — the one thing a Z report must
        // never do. Their frozen ExpectedCash / ActualCash / Variance are left untouched.
        //
        // Non-nullable with default 0 rather than nullable: NULL would mean "unknown", and the
        // historical value is not unknown, it is definitively "not tracked then". This also matches
        // the sibling column's shape so the two behave identically in queries and snapshots.
        migrationBuilder.AddColumn<decimal>(
            name: "CashExpenses",
            table: "CashRegisterSessions",
            type: "TEXT",
            precision: 18,
            scale: 2,
            nullable: false,
            defaultValue: 0m);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CashExpenses", table: "CashRegisterSessions");
    }
}
