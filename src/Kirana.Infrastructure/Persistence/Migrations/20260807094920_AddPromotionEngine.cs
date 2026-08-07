using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionEngine : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "PromotionDiscountTotal",
                table: "Sales",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PromotionDiscountAmount",
                table: "SaleItems",
                type: "TEXT",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromotionCode = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PromotionName = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    PromotionType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Percentage = table.Column<decimal>(type: "TEXT", precision: 5, scale: 2, nullable: true),
                    FlatAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    FixedPrice = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    PriorityMode = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CalculationMode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    AllowStacking = table.Column<bool>(type: "INTEGER", nullable: false),
                    MaximumDiscount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    MinimumBillAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: true),
                    MinimumQuantity = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: true),
                    MaximumUsage = table.Column<int>(type: "INTEGER", nullable: true),
                    CurrentUsage = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    UpdatedByUserId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promotions_Users_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Promotions_Users_UpdatedByUserId",
                        column: x => x.UpdatedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PromotionRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromotionId = table.Column<int>(type: "INTEGER", nullable: false),
                    RuleType = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    RuleValue = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionRules_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionSchedules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromotionId = table.Column<int>(type: "INTEGER", nullable: false),
                    StartAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DailyStartTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    DailyEndTime = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionSchedules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionSchedules_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromotionId = table.Column<int>(type: "INTEGER", nullable: false),
                    ScopeType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionScopes_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SaleItemPromotions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SaleItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    PromotionId = table.Column<int>(type: "INTEGER", nullable: false),
                    PromotionCodeSnapshot = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    PromotionNameSnapshot = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    PromotionTypeSnapshot = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    CalculationModeSnapshot = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    DiscountAmount = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SaleItemPromotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SaleItemPromotions_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SaleItemPromotions_SaleItems_SaleItemId",
                        column: x => x.SaleItemId,
                        principalTable: "SaleItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionTargets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PromotionScopeId = table.Column<int>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    BrandId = table.Column<int>(type: "INTEGER", nullable: true),
                    ProductId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PromotionTargets_Brands_BrandId",
                        column: x => x.BrandId,
                        principalTable: "Brands",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromotionTargets_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromotionTargets_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PromotionTargets_PromotionScopes_PromotionScopeId",
                        column: x => x.PromotionScopeId,
                        principalTable: "PromotionScopes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionRules_PromotionId",
                table: "PromotionRules",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_CreatedByUserId",
                table: "Promotions",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_IsActive_Status",
                table: "Promotions",
                columns: new[] { "IsActive", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_Priority",
                table: "Promotions",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_PromotionCode",
                table: "Promotions",
                column: "PromotionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_UpdatedByUserId",
                table: "Promotions",
                column: "UpdatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionSchedules_PromotionId",
                table: "PromotionSchedules",
                column: "PromotionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionSchedules_StartAtUtc_EndAtUtc",
                table: "PromotionSchedules",
                columns: new[] { "StartAtUtc", "EndAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionScopes_PromotionId",
                table: "PromotionScopes",
                column: "PromotionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTargets_BrandId",
                table: "PromotionTargets",
                column: "BrandId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTargets_CategoryId",
                table: "PromotionTargets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTargets_ProductId",
                table: "PromotionTargets",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTargets_PromotionScopeId_BrandId",
                table: "PromotionTargets",
                columns: new[] { "PromotionScopeId", "BrandId" },
                unique: true,
                filter: "\"BrandId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTargets_PromotionScopeId_CategoryId",
                table: "PromotionTargets",
                columns: new[] { "PromotionScopeId", "CategoryId" },
                unique: true,
                filter: "\"CategoryId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionTargets_PromotionScopeId_ProductId",
                table: "PromotionTargets",
                columns: new[] { "PromotionScopeId", "ProductId" },
                unique: true,
                filter: "\"ProductId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItemPromotions_PromotionId",
                table: "SaleItemPromotions",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_SaleItemPromotions_SaleItemId",
                table: "SaleItemPromotions",
                column: "SaleItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PromotionRules");

            migrationBuilder.DropTable(
                name: "PromotionSchedules");

            migrationBuilder.DropTable(
                name: "PromotionTargets");

            migrationBuilder.DropTable(
                name: "SaleItemPromotions");

            migrationBuilder.DropTable(
                name: "PromotionScopes");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropColumn(
                name: "PromotionDiscountTotal",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "PromotionDiscountAmount",
                table: "SaleItems");
        }
    }
}
