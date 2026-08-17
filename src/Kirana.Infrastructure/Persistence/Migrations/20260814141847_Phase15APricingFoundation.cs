using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 15A: adds the ProductPrices table — the authoritative store for selling prices — and
    /// backfills it from the columns that held them before.
    ///
    /// <para>Purely additive. Products, Sales, SaleItems, Purchases, Inventory, StockMovements and
    /// every Phase 13/14 table are left exactly as they are; in particular
    /// <c>Product.SellingPrice</c> and <c>Product.WholesalePrice</c> are NOT dropped or altered.
    /// They remain as synchronised projections so POS, reports and exports keep working unchanged,
    /// and that is also what makes this migration reversible without data loss.</para>
    ///
    /// <para>The backfill copies the price COLUMN TO COLUMN with no arithmetic, no CAST and no
    /// rounding. Money is stored as TEXT in this SQLite schema, so any computation could alter the
    /// stored representation of a value like 57.50 — a direct copy cannot. A regression test
    /// fingerprints every product's prices either side of the migration to prove they are identical.</para>
    ///
    /// <para>The unique index is created BEFORE the backfill on purpose — the opposite of the Phase
    /// 13B barcode migration. There, legacy case-only duplicates genuinely existed and had to be
    /// resolved by the INSERT itself. Here each product can yield at most one row per level by
    /// construction, so putting the index first turns any unexpected duplicate into a loud failed
    /// migration rather than silently bad pricing data.</para>
    /// </summary>
    public partial class Phase15APricingFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Level = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPrices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId",
                table: "ProductPrices",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId_Level_Active",
                table: "ProductPrices",
                columns: new[] { "ProductId", "Level" },
                unique: true,
                filter: "\"IsActive\" = 1");

            // ---- Retail backfill ----
            // Every existing product gets exactly one active Retail row. SellingPrice is copied
            // straight across: no ROUND, no CAST, no expression, so the stored text is byte-identical
            // and a price like 57.50 cannot become 57.5 or 57.
            //
            // Level is written as the enum MEMBER NAME to match the HasConversion<string>()
            // convention used across this schema.
            migrationBuilder.Sql("""
                INSERT INTO ProductPrices (ProductId, Level, Price, IsActive, CreatedAtUtc, UpdatedAtUtc)
                SELECT p.Id, 'Retail', p.SellingPrice, 1, CURRENT_TIMESTAMP, NULL
                FROM Products p;
                """);

            // ---- Wholesale backfill ----
            // Only where the level was actually configured. NULL means "wholesale does not apply"
            // and must NOT become a zero-priced row — while an explicit 0 IS a configured price and
            // must survive, which is why the filter tests IS NOT NULL rather than truthiness.
            migrationBuilder.Sql("""
                INSERT INTO ProductPrices (ProductId, Level, Price, IsActive, CreatedAtUtc, UpdatedAtUtc)
                SELECT p.Id, 'Wholesale', p.WholesalePrice, 1, CURRENT_TIMESTAMP, NULL
                FROM Products p
                WHERE p.WholesalePrice IS NOT NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductPrices");
        }
    }
}
