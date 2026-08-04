namespace Kirana.Application.Reports;

public sealed class InventoryRow
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string? CategoryName { get; init; }
    public decimal QuantityOnHand { get; init; }
    public string Unit { get; init; } = string.Empty;

    /// <summary>Null unless the caller holds <see cref="Domain.Entities.PermissionKeys.PricingViewPurchasePrice"/>.</summary>
    public decimal? StockValue { get; init; }
}

public sealed class InventoryValuationSummary
{
    public decimal TotalStockValue { get; init; }
    public int ProductCount { get; init; }
    public decimal TotalUnitsOnHand { get; init; }
}

public sealed class StockMovementRow
{
    public DateTime TimestampUtc { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string MovementType { get; init; } = string.Empty;
    public decimal QuantityChange { get; init; }
    public decimal NewQuantity { get; init; }
    public string? ReferenceType { get; init; }
    public string? ReferenceId { get; init; }
    public string? Reason { get; init; }
}

public sealed class BatchSummaryRow
{
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public string BatchNumber { get; init; } = string.Empty;
    public decimal Quantity { get; init; }
    public DateOnly? ManufacturingDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
    public bool IsExpired { get; init; }
}
