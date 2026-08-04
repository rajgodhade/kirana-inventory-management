using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerLedgerAndUdhaar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerCode",
                table: "Customers",
                type: "TEXT",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "Customers",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            // Existing databases already contain customers created before Customer IDs existed.
            // They land here with CustomerCode = '' which would collide on the unique index created
            // below, so backfill sequential CUST-000001.. codes by Id order first. A correlated
            // subquery is used rather than ROW_NUMBER()/UPDATE..FROM so this works on older SQLite.
            migrationBuilder.Sql(@"
                UPDATE Customers
                SET CustomerCode = 'CUST-' || substr('000000' ||
                    CAST((SELECT COUNT(*) FROM Customers c2 WHERE c2.Id <= Customers.Id) AS TEXT), -6, 6)
                WHERE CustomerCode IS NULL OR CustomerCode = '';");

            // Advance the Customer sequence past the codes just handed out, so the next customer
            // created in the app does not reuse one. NextValue is the value issued next, hence +1.
            migrationBuilder.Sql(@"
                INSERT INTO SequenceCounters (Key, NextValue)
                SELECT 'Customer', (SELECT COUNT(*) FROM Customers) + 1
                WHERE NOT EXISTS (SELECT 1 FROM SequenceCounters WHERE Key = 'Customer');");

            migrationBuilder.Sql(@"
                UPDATE SequenceCounters
                SET NextValue = (SELECT COUNT(*) FROM Customers) + 1
                WHERE Key = 'Customer' AND NextValue <= (SELECT COUNT(*) FROM Customers);");

            migrationBuilder.CreateTable(
                name: "CreditPayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReceiptNumber = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CustomerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    Method = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    ReferenceNumber = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    PaymentDateUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RecordedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditPayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditPayments_Customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreditPayments_Users_RecordedByUserId",
                        column: x => x.RecordedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreditPaymentAllocations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreditPaymentId = table.Column<int>(type: "INTEGER", nullable: false),
                    CustomerCreditId = table.Column<int>(type: "INTEGER", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreditPaymentAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreditPaymentAllocations_CreditPayments_CreditPaymentId",
                        column: x => x.CreditPaymentId,
                        principalTable: "CreditPayments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CreditPaymentAllocations_CustomerCredits_CustomerCreditId",
                        column: x => x.CustomerCreditId,
                        principalTable: "CustomerCredits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_CustomerCode",
                table: "Customers",
                column: "CustomerCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_Name",
                table: "Customers",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_CreditPaymentAllocations_CreditPaymentId",
                table: "CreditPaymentAllocations",
                column: "CreditPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditPaymentAllocations_CustomerCreditId",
                table: "CreditPaymentAllocations",
                column: "CustomerCreditId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditPayments_CustomerId",
                table: "CreditPayments",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CreditPayments_PaymentDateUtc",
                table: "CreditPayments",
                column: "PaymentDateUtc");

            migrationBuilder.CreateIndex(
                name: "IX_CreditPayments_ReceiptNumber",
                table: "CreditPayments",
                column: "ReceiptNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreditPayments_RecordedByUserId",
                table: "CreditPayments",
                column: "RecordedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CreditPaymentAllocations");

            migrationBuilder.DropTable(
                name: "CreditPayments");

            migrationBuilder.DropIndex(
                name: "IX_Customers_CustomerCode",
                table: "Customers");

            migrationBuilder.DropIndex(
                name: "IX_Customers_Name",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "CustomerCode",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "Customers");
        }
    }
}
