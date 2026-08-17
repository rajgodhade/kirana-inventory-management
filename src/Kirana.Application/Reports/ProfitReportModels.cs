namespace Kirana.Application.Reports;

/// <summary>
/// Profitability for a date range (PRD §51 "Profit Reports").
///
/// <para>Cost of goods sold comes from <see cref="Domain.Entities.SaleItem.UnitCostSnapshot"/> —
/// what each unit cost the shop at the moment it was sold (Phase 17A). Before that snapshot
/// existed, COGS was reconstructed from each product's <em>current</em> purchase price, which meant
/// repricing a product today retroactively changed the profit reported for past periods. Historical
/// profit is now a fact rather than a moving figure.</para>
///
/// <para><b>Sales predating the snapshot have no recorded cost</b>, and those lines are excluded
/// from COGS rather than counted at zero — see <see cref="UnknownCostLineCount"/>. A period
/// containing them reports a cost that covers only part of what it sold, so the two line counts
/// travel with the figures and must be shown wherever the profit is.</para>
/// </summary>
public sealed class ProfitSummary
{
    public required ReportDateRange Range { get; init; }

    /// <summary>Gross sales for the period minus goods returned in the period.</summary>
    public decimal Revenue { get; init; }

    public decimal GrossSales { get; init; }
    public decimal Returns { get; init; }

    /// <summary>Quantity sold × the cost snapshotted on each line at sale time, netted against the
    /// same for returned quantities. Covers only <see cref="KnownCostLineCount"/> lines.</summary>
    public decimal CostOfGoodsSold { get; init; }

    /// <summary>Sale lines in the period that carry a cost snapshot and are therefore included in
    /// <see cref="CostOfGoodsSold"/>.</summary>
    public int KnownCostLineCount { get; init; }

    /// <summary>Sale lines in the period with no recorded cost — sales made before Phase 17A. They
    /// contribute revenue but no cost, so while this is above zero the gross profit shown is an
    /// upper bound, not the real figure.</summary>
    public int UnknownCostLineCount { get; init; }

    /// <summary>True when every line in the period has a recorded cost, and the profit figures are
    /// therefore complete rather than partial.</summary>
    public bool HasCompleteCostBasis => UnknownCostLineCount == 0;

    public decimal GrossProfit { get; init; }
    public decimal Expenses { get; init; }
    public decimal NetProfit { get; init; }
}
