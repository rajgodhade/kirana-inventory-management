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

    /// <summary>Stock added by an authorized manual correction (Phase 13D) — found goods, an
    /// opening-balance fix, or compensating an earlier mistake. Carries an "ADJ-…" reference to the
    /// <see cref="InventoryAdjustment"/> holding the reason and notes.
    /// <para>Distinct from <see cref="StockCountIncrease"/> (evidence from counting a shelf) and
    /// from the legacy <see cref="PositiveAdjustment"/> (kept only so historical rows keep their
    /// meaning; nothing writes it any more).</para></summary>
    InventoryAdjustmentIncrease,

    /// <summary>Stock removed by an authorized manual correction (Phase 13D) — damage, expiry,
    /// loss, or theft. See <see cref="InventoryAdjustmentIncrease"/>.
    /// <para>Deliberately NOT <see cref="Damaged"/>, even for damage: that type is written by the
    /// sales-return flow and feeds the damaged-stock report, so reusing it here would mix
    /// goods-returned-broken with shelf breakage and make both figures untrustworthy.</para></summary>
    InventoryAdjustmentDecrease,
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
        StockMovementType.InventoryAdjustmentIncrease,
    ];

    public static bool IsIncrease(this StockMovementType type) => IncreaseTypes.Contains(type);
}
