using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 13B: replaces the single Products.Barcode column with a ProductBarcodes table, so one
    /// product can carry a manufacturer EAN, a repack EAN, and an internal code that all scan to it.
    ///
    /// <para>The step order matters and is NOT what `dotnet ef` scaffolds. EF put the DropColumn
    /// first, which would have destroyed every existing barcode; the column must survive until its
    /// values have been copied. Correct order: create table → backfill → create indexes → drop the
    /// old index → drop the column.</para>
    ///
    /// <para>The unique index is created AFTER the backfill on purpose. The old index on
    /// Products.Barcode was case-SENSITIVE, so a legacy pair like "abc123"/"ABC123" could coexist
    /// there while violating this table's case-insensitive uniqueness. Resolving that with GROUP BY
    /// inside the INSERT keeps the migration non-interactive; creating the index first would instead
    /// abort the upgrade half-applied.</para>
    ///
    /// <para>Every migrated code becomes its product's PRIMARY, ACTIVE barcode, so behavior is
    /// preserved one-for-one: whatever scanned to a product before still does, and label printing
    /// still defaults to the same value. Products with a NULL/blank barcode correctly get no row.</para>
    ///
    /// <para>The column also narrows from 60 to 48 characters, matching the limit BarcodeService
    /// has always actually enforced — 49-60 was unreachable dead headroom, so no data can be lost.</para>
    ///
    /// <para>Note the EnsureCreated()-based test fixtures build straight from the model and never run
    /// this backfill; it is covered by a dedicated test that migrates the previous migration, inserts
    /// legacy rows, then migrates forward — plus a rehearsal against a copy of the real database.</para>
    /// </summary>
    public partial class AddProductBarcodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductBarcodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    NormalizedValue = table.Column<string>(type: "TEXT", maxLength: 48, nullable: false),
                    Symbology = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsPrimary = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsInternal = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductBarcodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductBarcodes_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Backfill. GROUP BY on the upper-cased value defends against legacy case-only
            // duplicates the old case-sensitive index allowed; MIN(Id) keeps the lowest-numbered
            // product and the loser is left with no barcode — visible in the UI as a product to
            // re-code, rather than a failed migration or a code silently pointing at the wrong item.
            //
            // The bare TRIM(p.Barcode) columns alongside MIN(p.Id) are safe ONLY because of a
            // documented SQLite guarantee: in a query with a single MIN()/MAX() aggregate, bare
            // columns take their values from the row that produced that min/max. So the stored value
            // always belongs to the product whose Id is kept. Removing MIN(), adding a second
            // aggregate, or porting this to another engine breaks that guarantee and can pair one
            // product's Id with another's barcode text. Pinned by
            // MigrationSchemaTests.BarcodeBackfill_KeepsValueAndProductFromTheSameRow_WhenResolvingDuplicates.
            migrationBuilder.Sql("""
                INSERT INTO ProductBarcodes
                    (ProductId, Value, NormalizedValue, Symbology, IsPrimary, IsInternal, IsActive, Notes, CreatedAtUtc, UpdatedAtUtc)
                SELECT
                    MIN(p.Id),
                    TRIM(p.Barcode),
                    UPPER(TRIM(p.Barcode)),
                    'Code128',
                    1,
                    CASE WHEN SUBSTR(TRIM(p.Barcode), 1, 2) = '20' AND LENGTH(TRIM(p.Barcode)) = 13
                         THEN 1 ELSE 0 END,
                    1,
                    NULL,
                    CURRENT_TIMESTAMP,
                    NULL
                FROM Products p
                WHERE p.Barcode IS NOT NULL AND TRIM(p.Barcode) <> ''
                GROUP BY UPPER(TRIM(p.Barcode));
                """);

            // Symbology refinement. DetermineSymbology() requires a valid EAN-13 check digit, which
            // isn't reasonably computable in SQLite SQL. Treating every 13-digit code as EAN-13 is
            // safe: the renderer draws a 13-digit value as EAN-13 regardless, and the value is
            // re-derived properly the next time the barcode is edited. The only divergence is a
            // 13-digit code with a bad check digit, and that is cosmetic — it never affects lookup.
            migrationBuilder.Sql("""
                UPDATE ProductBarcodes
                SET Symbology = 'Ean13'
                WHERE LENGTH(Value) = 13
                  AND Value GLOB '[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9]';
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_IsActive_NormalizedValue",
                table: "ProductBarcodes",
                columns: new[] { "IsActive", "NormalizedValue" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_NormalizedValue",
                table: "ProductBarcodes",
                column: "NormalizedValue",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_ProductId",
                table: "ProductBarcodes",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProductBarcodes_ProductId_Primary",
                table: "ProductBarcodes",
                column: "ProductId",
                unique: true,
                filter: "\"IsPrimary\" = 1");

            // Only now that every value is safely copied.
            migrationBuilder.DropIndex(
                name: "IX_Products_Barcode",
                table: "Products");

            migrationBuilder.DropColumn(
                name: "Barcode",
                table: "Products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restored at its original width (60), not the new 48 — Down() must reproduce the prior
            // schema exactly, not "fix" it.
            migrationBuilder.AddColumn<string>(
                name: "Barcode",
                table: "Products",
                type: "TEXT",
                maxLength: 60,
                nullable: true);

            // Only the primary active code survives: the single-barcode schema has room for exactly
            // one. Alternates are lost with the table — inherent to reverting to a one-barcode model,
            // which is why this is a recovery path rather than a routine one.
            migrationBuilder.Sql("""
                UPDATE Products
                SET Barcode = (
                    SELECT pb.Value FROM ProductBarcodes pb
                    WHERE pb.ProductId = Products.Id AND pb.IsPrimary = 1 AND pb.IsActive = 1
                    LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Products_Barcode",
                table: "Products",
                column: "Barcode",
                unique: true,
                filter: "\"Barcode\" IS NOT NULL");

            migrationBuilder.DropTable(
                name: "ProductBarcodes");
        }
    }
}
