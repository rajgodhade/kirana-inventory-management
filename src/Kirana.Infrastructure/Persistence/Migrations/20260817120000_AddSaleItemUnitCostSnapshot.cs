using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations;

[DbContext(typeof(KiranaDbContext))]
[Migration("20260817120000_AddSaleItemUnitCostSnapshot")]
public partial class AddSaleItemUnitCostSnapshot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // What a unit cost the shop when it was sold, so historical profit stops being recomputed
        // from today's master data. Profit previously multiplied quantity by the product's CURRENT
        // PurchasePrice, which meant raising a cost today silently rewrote last month's reported
        // profit.
        //
        // NULLABLE, with no default and no backfill. Null means "cost unknown for this line" — the
        // honest state of every sale recorded before this column existed. Three things follow:
        //
        //   * A default of 0 would report those lines at 100% margin. That is not a conservative
        //     fallback, it is a wrong number that looks like a right one.
        //   * Backfilling from Product.PurchasePrice would fabricate history: today's cost is not
        //     evidence of what the shop paid a year ago, and once written it would be
        //     indistinguishable from a genuinely captured cost.
        //   * Reports therefore exclude null-cost lines from COGS and report how many they
        //     excluded, rather than folding them in silently.
        //
        // Additive column only: no data transformation, no rewrite of any existing SaleItem, and
        // every historical money value is preserved exactly as recorded.
        migrationBuilder.AddColumn<decimal>(
            name: "UnitCostSnapshot",
            table: "SaleItems",
            type: "TEXT",
            precision: 18,
            scale: 2,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Raw SQL rather than migrationBuilder.DropColumn. EF's SQLite generator implements
        // DropColumn by rebuilding the table, and it refuses to do that for SaleItems while
        // migrating DOWN — SalesReturnItems holds a foreign key into it, so the rebuild has no
        // model to reconstruct against and the operation is reported as unsupported. (The existing
        // barcode down-migration test walks the whole chain backwards and is what surfaces this.)
        //
        // SQLite has supported ALTER TABLE ... DROP COLUMN natively since 3.35, so dropping it
        // directly is both simpler and genuinely reversible. Nothing else references this column.
        migrationBuilder.Sql("ALTER TABLE \"SaleItems\" DROP COLUMN \"UnitCostSnapshot\";");
    }
}
