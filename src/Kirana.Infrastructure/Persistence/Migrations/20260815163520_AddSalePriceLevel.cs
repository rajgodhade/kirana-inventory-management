using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kirana.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Phase 15B-5: records which price level a completed bill was actually sold at.
    ///
    /// <para>Bill-wide, matching how the level is chosen — there is no per-line level and a sale has
    /// exactly one. The column stores the pricing CONTEXT; SaleItem.UnitPriceSnapshot remains
    /// authoritative for what each line cost.</para>
    ///
    /// <para><b>Existing sales are backfilled to 'Retail'.</b> This is a labelling policy, not a
    /// finding. Sales completed before this column existed never stored their pricing context, and
    /// it cannot be reconstructed afterwards: comparing a snapshot against today's ProductPrice,
    /// the customer's current preference, or the product's projection columns would all be guesses
    /// against values that have since moved. Retail is chosen because it is what the till did for
    /// every one of those sales — price-level selection did not exist yet — so the label is
    /// accurate in practice even though it is not evidence.</para>
    ///
    /// <para>EF scaffolded this with <c>defaultValue: ""</c>, which would have written an empty
    /// string — a value that is not a member of the enum — into every historical row. Replaced with
    /// 'Retail' so the backfill produces a valid, readable level. The default also stays on the
    /// column, which is harmless: EF always writes the value explicitly, so it only ever applies to
    /// a raw INSERT that forgot one.</para>
    ///
    /// <para>Purely additive. SQLite adds a defaulted column in place rather than rebuilding the
    /// table, so sale totals, dates, items, payments and every other business table are untouched —
    /// verified by fingerprinting each table either side of the migration.</para>
    /// </summary>
    public partial class AddSalePriceLevel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PriceLevel",
                table: "Sales",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Retail");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PriceLevel",
                table: "Sales");
        }
    }
}
