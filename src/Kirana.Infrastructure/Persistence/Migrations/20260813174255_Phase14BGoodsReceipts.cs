using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase14BGoodsReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "GoodsReceiptId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PurchaseOrderId",
                table: "Purchases",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GoodsReceipts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GoodsReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    PurchaseOrderId = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplierId = table.Column<int>(type: "INTEGER", nullable: false),
                    SupplierNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SupplierCodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    ReceivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancelledByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CancellationReason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceipts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_PurchaseOrders_PurchaseOrderId",
                        column: x => x.PurchaseOrderId,
                        principalTable: "PurchaseOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Suppliers_SupplierId",
                        column: x => x.SupplierId,
                        principalTable: "Suppliers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Users_CancelledByUserId",
                        column: x => x.CancelledByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Users_CompletedByUserId",
                        column: x => x.CompletedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceipts_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "GoodsReceiptItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    GoodsReceiptId = table.Column<int>(type: "INTEGER", nullable: false),
                    PurchaseOrderItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductCodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    OrderedQuantitySnapshot = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    ReceivedQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    BarcodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 48, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GoodsReceiptItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptItems_GoodsReceipts_GoodsReceiptId",
                        column: x => x.GoodsReceiptId,
                        principalTable: "GoodsReceipts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_GoodsReceiptItems_PurchaseOrderItems_PurchaseOrderItemId",
                        column: x => x.PurchaseOrderItemId,
                        principalTable: "PurchaseOrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_GoodsReceiptId",
                table: "Purchases",
                column: "GoodsReceiptId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Purchases_PurchaseOrderId",
                table: "Purchases",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_GoodsReceiptId",
                table: "GoodsReceiptItems",
                column: "GoodsReceiptId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_ProductId",
                table: "GoodsReceiptItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceiptItems_PurchaseOrderItemId",
                table: "GoodsReceiptItems",
                column: "PurchaseOrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CancelledByUserId",
                table: "GoodsReceipts",
                column: "CancelledByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CompletedByUserId",
                table: "GoodsReceipts",
                column: "CompletedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_CreatedByUserId",
                table: "GoodsReceipts",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_GoodsReceiptNumber",
                table: "GoodsReceipts",
                column: "GoodsReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_PurchaseOrderId",
                table: "GoodsReceipts",
                column: "PurchaseOrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_ReceivedAtUtc",
                table: "GoodsReceipts",
                column: "ReceivedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_Status",
                table: "GoodsReceipts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_GoodsReceipts_SupplierId",
                table: "GoodsReceipts",
                column: "SupplierId");

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_GoodsReceipts_GoodsReceiptId",
                table: "Purchases",
                column: "GoodsReceiptId",
                principalTable: "GoodsReceipts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Purchases_PurchaseOrders_PurchaseOrderId",
                table: "Purchases",
                column: "PurchaseOrderId",
                principalTable: "PurchaseOrders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_GoodsReceipts_GoodsReceiptId",
                table: "Purchases");

            migrationBuilder.DropForeignKey(
                name: "FK_Purchases_PurchaseOrders_PurchaseOrderId",
                table: "Purchases");

            migrationBuilder.DropTable(
                name: "GoodsReceiptItems");

            migrationBuilder.DropTable(
                name: "GoodsReceipts");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_GoodsReceiptId",
                table: "Purchases");

            migrationBuilder.DropIndex(
                name: "IX_Purchases_PurchaseOrderId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "GoodsReceiptId",
                table: "Purchases");

            migrationBuilder.DropColumn(
                name: "PurchaseOrderId",
                table: "Purchases");
        }
    }
}
