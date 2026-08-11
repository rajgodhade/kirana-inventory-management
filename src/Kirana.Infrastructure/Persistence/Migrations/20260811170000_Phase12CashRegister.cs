using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations;

[DbContext(typeof(KiranaDbContext))]
[Migration("20260811170000_Phase12CashRegister")]
public partial class Phase12CashRegister : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "CashRegisterSessions",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                StoreId = table.Column<int>(type: "INTEGER", nullable: false),
                RegisterName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                BusinessDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                OpenedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                OpenedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                OpeningCash = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                ClosedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                ClosedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                TotalSales = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                BillCount = table.Column<int>(type: "INTEGER", nullable: false),
                CashSales = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                UpiSales = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                CardSales = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                CustomerCreditSales = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                TotalReturns = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                CashRefunds = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                CashCreditRepayments = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                CashIn = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                CashOut = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                ExpectedCash = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                ActualCash = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                Variance = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CashRegisterSessions", x => x.Id);
                table.ForeignKey("FK_CashRegisterSessions_Stores_StoreId", x => x.StoreId, "Stores", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CashRegisterSessions_Users_OpenedByUserId", x => x.OpenedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CashRegisterSessions_Users_ClosedByUserId", x => x.ClosedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "CashMovements",
            columns: table => new
            {
                Id = table.Column<int>(type: "INTEGER", nullable: false).Annotation("Sqlite:Autoincrement", true),
                RegisterSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                Amount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                Reason = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                PerformedByUserId = table.Column<int>(type: "INTEGER", nullable: false),
                OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_CashMovements", x => x.Id);
                table.ForeignKey("FK_CashMovements_CashRegisterSessions_RegisterSessionId", x => x.RegisterSessionId, "CashRegisterSessions", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_CashMovements_Users_PerformedByUserId", x => x.PerformedByUserId, "Users", "Id", onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex("IX_CashMovements_OperationId", "CashMovements", "OperationId", unique: true);
        migrationBuilder.CreateIndex("IX_CashMovements_PerformedByUserId", "CashMovements", "PerformedByUserId");
        migrationBuilder.CreateIndex("IX_CashMovements_RegisterSessionId_OccurredAtUtc", "CashMovements", new[] { "RegisterSessionId", "OccurredAtUtc" });
        migrationBuilder.CreateIndex("IX_CashRegisterSessions_BusinessDate", "CashRegisterSessions", "BusinessDate");
        migrationBuilder.CreateIndex("IX_CashRegisterSessions_ClosedByUserId", "CashRegisterSessions", "ClosedByUserId");
        migrationBuilder.CreateIndex("IX_CashRegisterSessions_OpenedAtUtc", "CashRegisterSessions", "OpenedAtUtc");
        migrationBuilder.CreateIndex("IX_CashRegisterSessions_OpenedByUserId", "CashRegisterSessions", "OpenedByUserId");
        migrationBuilder.CreateIndex("IX_CashRegisterSessions_StoreId_Status", "CashRegisterSessions", new[] { "StoreId", "Status" });
        migrationBuilder.CreateIndex("IX_CashRegisterSessions_StoreId_Open", "CashRegisterSessions", "StoreId", unique: true, filter: "Status = 'Open'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("CashMovements");
        migrationBuilder.DropTable("CashRegisterSessions");
    }
}
