namespace Kirana.Application.Reports;

/// <summary>
/// Estimated profitability for a date range (PRD §51 "Profit Reports"). Called "estimated"
/// throughout the PRD deliberately: <c>SaleItem</c> snapshots the price the customer paid but not
/// the product's cost at the time of sale (only pricing fields relevant to the sale itself were
/// captured — see <see cref="Domain.Entities.SaleItem"/>), so cost of goods sold is reconstructed
/// from each product's <em>current</em> purchase price rather than a true historical cost basis.
/// This is the same limitation every report in this phase inherits; it is not something a
/// reporting phase should fix by retrofitting a new snapshot column onto historical rows.
/// </summary>
public sealed class ProfitSummary
{
    public required ReportDateRange Range { get; init; }

    /// <summary>Gross sales for the period minus goods returned in the period.</summary>
    public decimal Revenue { get; init; }

    public decimal GrossSales { get; init; }
    public decimal Returns { get; init; }

    /// <summary>Quantity sold × each product's current purchase price, netted against the same for
    /// returned quantities.</summary>
    public decimal CostOfGoodsSold { get; init; }

    public decimal GrossProfit { get; init; }
    public decimal Expenses { get; init; }
    public decimal NetProfit { get; init; }
}
