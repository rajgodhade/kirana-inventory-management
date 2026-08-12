namespace Kirana.Domain.Entities;

/// <summary>Stock increase/decrease reasons recognized by the ledger (PRD §24-25).</summary>
public enum StockMovementType
{
    OpeningStock,
    Purchase,
    SalesReturn,
    PositiveAdjustment,
    Sale,
    PurchaseReturn,
    Damaged,
    Expired,
    NegativeAdjustment,

    /// <summary>Surplus found by a physical stock count (Phase 13C). Distinct from
    /// <see cref="PositiveAdjustment"/> so the ledger can tell "a stock-take found more on the
    /// shelf" apart from "someone corrected a number by hand" — they have different credibility
    /// when investigating shrinkage, and only the former carries a count number to trace back to.</summary>
    StockCountIncrease,

    /// <summary>Shortage found by a physical stock count (Phase 13C). See
    /// <see cref="StockCountIncrease"/> for why this is not just <see cref="NegativeAdjustment"/>.</summary>
    StockCountDecrease,
}

public static class StockMovementTypeExtensions
{
    private static readonly HashSet<StockMovementType> IncreaseTypes =
    [
        StockMovementType.OpeningStock,
        StockMovementType.Purchase,
        StockMovementType.SalesReturn,
        StockMovementType.PositiveAdjustment,
        StockMovementType.StockCountIncrease,
    ];

    public static bool IsIncrease(this StockMovementType type) => IncreaseTypes.Contains(type);
}
