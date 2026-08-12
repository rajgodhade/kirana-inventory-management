using Kirana.Domain.Entities;

namespace Kirana.Application.StockCounts;

/// <summary>Row for the stock-count list page. Projected in the query rather than materializing
/// every item, so the history page stays cheap as counts accumulate.</summary>
public sealed record StockCountSummary(
    int Id,
    string CountNumber,
    StockCountStatus Status,
    DateTime StartedAtUtc,
    DateTime? CompletedAtUtc,
    string? StartedByUserName,
    int ItemCount,
    int CountedItemCount,
    int VarianceItemCount);

/// <summary>
/// What finalization is about to do, computed without writing anything (Phase 13C §19). Backs the
/// variance-review screen so the operator confirms real numbers rather than a blind
/// "Complete Count" button.
/// </summary>
public sealed record StockCountVariancePreview(
    int StockCountId,
    string CountNumber,
    int TotalItems,
    int CountedItems,
    int UncountedItems,
    int IncreaseCount,
    int DecreaseCount,
    int UnchangedCount,
    decimal TotalIncreaseQuantity,
    decimal TotalDecreaseQuantity,
    IReadOnlyList<StockCountVarianceLine> Lines)
{
    /// <summary>Items whose live stock moved after the count started. Their adjustment will be
    /// rebased onto current stock, so the operator is told rather than silently corrected.</summary>
    public IReadOnlyList<StockCountVarianceLine> RebasedLines =>
        Lines.Where(l => l.WillRebase).ToList();

    public bool HasRebases => Lines.Any(l => l.WillRebase);
    public int AdjustmentCount => IncreaseCount + DecreaseCount;
}

/// <summary>
/// One line of the variance review. <paramref name="SystemQuantity"/> is the count-time snapshot the
/// counter compared against; <paramref name="CurrentSystemQuantity"/> is what inventory says right
/// now. They differ only when stock moved mid-count.
/// </summary>
public sealed record StockCountVarianceLine(
    int StockCountItemId,
    int ProductId,
    string ProductName,
    string ProductCode,
    UnitOfMeasure Unit,
    decimal SystemQuantity,
    decimal? CountedQuantity,
    decimal CurrentSystemQuantity)
{
    /// <summary>What the counter observed: physical minus the snapshot.</summary>
    public decimal? ObservedVariance => CountedQuantity is null ? null : CountedQuantity - SystemQuantity;

    /// <summary>What will actually be applied to inventory. Rebased onto CURRENT stock so the
    /// result always lands on the counted figure — applying the observed variance to already-moved
    /// stock would overshoot (snapshot 100, now 98, counted 97: −3 would land on 95, not 97).</summary>
    public decimal AppliedAdjustment =>
        CountedQuantity is null ? 0m : CountedQuantity.Value - CurrentSystemQuantity;

    /// <summary>True when live stock moved since the snapshot, so this line's adjustment differs
    /// from the variance the counter saw.</summary>
    public bool WillRebase => CountedQuantity is not null && CurrentSystemQuantity != SystemQuantity;

    /// <summary>Stock on hand once this line is applied — always the counted figure.</summary>
    public decimal ResultingQuantity => CountedQuantity ?? CurrentSystemQuantity;
}

/// <summary>Outcome of a successful finalization, for the completion screen (§20).</summary>
public sealed record StockCountResult(
    int StockCountId,
    string CountNumber,
    int ProductsCounted,
    int IncreasedCount,
    int DecreasedCount,
    int UnchangedCount,
    int AdjustmentCount,
    int RebasedCount,
    decimal TotalIncreaseQuantity,
    decimal TotalDecreaseQuantity);
