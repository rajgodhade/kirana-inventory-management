using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Products.IsTaxInclusive was the original NOT NULL boolean column (InitialCreate/
    /// AddProductAndInventory). AddProductPricingType (2026-08-08) replaced it with PricingType but
    /// never dropped the old column, and Product.IsTaxInclusive has been a [NotMapped] compatibility
    /// property ever since — so EF's generated INSERT never supplies it, and every new product
    /// creation fails with "NOT NULL constraint failed: Products.IsTaxInclusive" against any
    /// database that actually ran the migration chain (not the EnsureCreated()-based test fixtures,
    /// which build straight from the current model and never had this column to begin with — that's
    /// why this went unnoticed until a live run against a migrated database). Nothing reads this
    /// column any more (confirmed via repo-wide search), so dropping it is safe.
    /// </summary>
    public partial class DropLegacyProductIsTaxInclusiveColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTaxInclusive",
                table: "Products");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTaxInclusive",
                table: "Products",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);
        }
    }
}
