using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 15B-4: gives a customer an optional POS default price level.
    ///
    /// <para>One nullable column, nothing else. NULL is the meaningful default — it says "nobody has
    /// classified this customer", which is why there is deliberately NO backfill: writing 'Retail'
    /// into every existing row would fabricate a decision no one made, and would be
    /// indistinguishable afterwards from a real one. Both open a bill at Retail today, so the
    /// backfill would buy nothing and lose information.</para>
    ///
    /// <para>Purely additive, and SQLite adds a nullable column in place rather than rebuilding the
    /// table — so existing customer rows, their credit balances, and every sale, price and stock
    /// record are untouched. Verified by fingerprinting every table either side of the migration.</para>
    ///
    /// <para>This column is a <b>preference</b>, never a pricing authority: the bill's own level
    /// (and ultimately <c>CompleteSaleRequest.PriceLevel</c>) decides what is charged. Nothing in
    /// sale resolution reads it.</para>
    /// </summary>
    public partial class AddCustomerDefaultPriceLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DefaultPriceLevel",
                table: "Customers",
                type: "TEXT",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultPriceLevel",
                table: "Customers");
        }
    }
}
