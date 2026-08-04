using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPosAndBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Phone = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    Address = table.Column<string>(type: "TEXT", nullable: true),
                    Gstin = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CreditBalance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HeldBills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HeldAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CashierUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    BillDiscountPercent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeldBills", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HeldBills_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HeldBills_Users_CashierUserId",
                        column: x => x.CashierUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Sales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    InvoiceNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SaleDateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: true),
                    CashierUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    SubTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ItemDiscountTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    BillDiscountPercent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    BillDiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxableTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RoundOffAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    GrandTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DiscountAuthorizedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Sales_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Users_CashierUserId",
                        column: x => x.CashierUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Sales_Users_DiscountAuthorizedByUserId",
                        column: x => x.DiscountAuthorizedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HeldBillItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HeldBillId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HeldBillItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HeldBillItems_HeldBills_HeldBillId",
                        column: x => x.HeldBillId,
                        principalTable: "HeldBills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HeldBillItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCredits",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    SaleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    RemainingAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCredits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomerCredits_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCredits_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SaleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    AmountTendered = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    ChangeGiven = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SaleId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProductNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ProductCodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    SkuSnapshot = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    HsnCodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    UnitSnapshot = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsTaxInclusiveSnapshot = table.Column<bool>(type: "INTEGER", nullable: false),
                    GstRatePercentSnapshot = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    Quantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    UnitPriceSnapshot = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DiscountPercent = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    TaxableAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    GstAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    LineTotal = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleItems_Sales_SaleId",
                        column: x => x.SaleId,
                        principalTable: "Sales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCredits_CustomerId",
                table: "CustomerCredits",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCredits_SaleId",
                table: "CustomerCredits",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Phone",
                table: "Customers",
                column: "Phone",
                unique: true,
                filter: "\"Phone\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HeldBillItems_HeldBillId",
                table: "HeldBillItems",
                column: "HeldBillId");

            migrationBuilder.CreateIndex(
                name: "IX_HeldBillItems_ProductId",
                table: "HeldBillItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_HeldBills_CashierUserId",
                table: "HeldBills",
                column: "CashierUserId");

            migrationBuilder.CreateIndex(
                name: "IX_HeldBills_CustomerId",
                table: "HeldBills",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_HeldBills_HeldAtUtc",
                table: "HeldBills",
                column: "HeldAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_SaleId",
                table: "Payments",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItems_ProductId",
                table: "SaleItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItems_SaleId",
                table: "SaleItems",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CashierUserId",
                table: "Sales",
                column: "CashierUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_CustomerId",
                table: "Sales",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_DiscountAuthorizedByUserId",
                table: "Sales",
                column: "DiscountAuthorizedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Sales_InvoiceNumber",
                table: "Sales",
                column: "InvoiceNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Sales_SaleDateUtc",
                table: "Sales",
                column: "SaleDateUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerCredits");

            migrationBuilder.DropTable(
                name: "HeldBillItems");

            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "SaleItems");

            migrationBuilder.DropTable(
                name: "HeldBills");

            migrationBuilder.DropTable(
                name: "Sales");

            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
