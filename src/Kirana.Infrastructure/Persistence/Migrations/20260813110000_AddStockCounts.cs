using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 13C: adds the StockCounts / StockCountItems tables for physical stock counting.
    ///
    /// <para>Purely additive — it creates two new tables and touches nothing that already exists.
    /// In particular it does NOT alter StockMovements, Inventories, or any historical row: a stock
    /// count produces new adjustment movements when it is finalized, and never rewrites the ledger.
    /// The two new StockMovementType values (StockCountIncrease/StockCountDecrease) need no schema
    /// change because the column is a string conversion, so existing rows keep their exact values.</para>
    ///
    /// <para>IX_StockCounts_SingleInProgress is a filtered unique index on Status, which is what
    /// makes "only one count open at a time" a database invariant rather than a check-then-insert
    /// race in the service. Note SQLite evaluates such filtered indexes per STATEMENT — see
    /// StockCountService.FinalizeAsync for why status transitions are ordered carefully.</para>
    /// </summary>
    public partial class AddStockCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "StockCounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CountNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RebasedItemCount = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCounts_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCounts_Users_StartedByUserId",
                        column: x => x.StartedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StockCountItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    StockCountId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductCodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    BarcodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    SystemQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true),
                    SystemQuantityAtFinalization = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true),
                    CountedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockCountItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockCountItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_StockCountItems_StockCounts_StockCountId",
                        column: x => x.StockCountId,
                        principalTable: "StockCounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockCountItems_ProductId",
                table: "StockCountItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCountItems_StockCountId_ProductId",
                table: "StockCountItems",
                columns: new[] { "StockCountId", "ProductId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_CompletedByUserId",
                table: "StockCounts",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_CountNumber",
                table: "StockCounts",
                column: "CountNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_SingleInProgress",
                table: "StockCounts",
                column: "Status",
                unique: true,
                filter: "\"Status\" = 'InProgress'");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_StartedAtUtc",
                table: "StockCounts",
                column: "StartedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_StockCounts_StartedByUserId",
                table: "StockCounts",
                column: "StartedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "StockCountItems");

            migrationBuilder.DropTable(
                name: "StockCounts");
        }
    }
}
