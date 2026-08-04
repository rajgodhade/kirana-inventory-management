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
}

public static class StockMovementTypeExtensions
{
    private static readonly HashSet<StockMovementType> IncreaseTypes =
    [
        StockMovementType.OpeningStock,
        StockMovementType.Purchase,
        StockMovementType.SalesReturn,
        StockMovementType.PositiveAdjustment,
    ];

    public static bool IsIncrease(this StockMovementType type) => IncreaseTypes.Contains(type);
}
