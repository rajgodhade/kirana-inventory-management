using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 13D: adds the InventoryAdjustments table for authorized manual stock corrections.
    ///
    /// <para>Purely additive — one new table, and nothing that already exists is altered. In
    /// particular it does NOT touch StockMovements, Inventories, StockCounts or Products: an
    /// adjustment appends new rows to the ledger and never rewrites history. The two new
    /// StockMovementType values (InventoryAdjustmentIncrease/Decrease) need no schema change because
    /// the column is a string conversion, so existing movement rows keep their exact values — the
    /// Phase 13C stock-count types included.</para>
    ///
    /// <para>AdjustmentNumber is uniquely indexed so an "ADJ-…" reference on a stock movement always
    /// resolves to exactly one adjustment record.</para>
    /// </summary>
    public partial class AddInventoryAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryAdjustments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdjustmentNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductCodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Direction = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AdjustmentQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    PreviousQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    NewQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AdjustedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AdjustedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAdjustments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustments_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryAdjustments_Users_AdjustedByUserId",
                        column: x => x.AdjustedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_AdjustedAtUtc",
                table: "InventoryAdjustments",
                column: "AdjustedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_AdjustedByUserId",
                table: "InventoryAdjustments",
                column: "AdjustedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_AdjustmentNumber",
                table: "InventoryAdjustments",
                column: "AdjustmentNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_ProductId_AdjustedAtUtc",
                table: "InventoryAdjustments",
                columns: new[] { "ProductId", "AdjustedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAdjustments_Reason",
                table: "InventoryAdjustments",
                column: "Reason");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAdjustments");
        }
    }
}
