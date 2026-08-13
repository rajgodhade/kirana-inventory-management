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

/// <summary>
/// How much stock each correction mechanism moved over a period (Phase 13D §24). Answers "was this
/// found by counting the shelves, or asserted by hand?" — the two carry very different weight in a
/// shrinkage investigation, which is why they have separate movement types.
/// </summary>
public sealed class StockCorrectionSummaryRow
{
    /// <summary>"Physical stock count" or "Manual adjustment".</summary>
    public string Source { get; init; } = string.Empty;

    /// <summary>Reason label for manual adjustments; empty for stock counts, which have no reason
    /// beyond "this is what was on the shelf".</summary>
    public string Reason { get; init; } = string.Empty;

    public int MovementCount { get; init; }
    public decimal TotalIncreaseQuantity { get; init; }
    public decimal TotalDecreaseQuantity { get; init; }

    public decimal NetQuantityChange => TotalIncreaseQuantity - TotalDecreaseQuantity;
}

/// <summary>One completed physical stock count and what it adjusted (Phase 13C §22).</summary>
public sealed class StockCountReportRow
{
    public string CountNumber { get; init; } = string.Empty;
    public DateTime StartedAtUtc { get; init; }
    public DateTime? CompletedAtUtc { get; init; }
    public string Status { get; init; } = string.Empty;
    public string? CountedBy { get; init; }

    public int ProductsCounted { get; init; }
    public int IncreasedCount { get; init; }
    public int DecreasedCount { get; init; }
    public int UnchangedCount { get; init; }

    /// <summary>Sum of the surpluses/shortages actually applied to inventory. Zero for a cancelled
    /// count, which by definition adjusted nothing.</summary>
    public decimal TotalIncreaseQuantity { get; init; }
    public decimal TotalDecreaseQuantity { get; init; }

    /// <summary>Net effect on stock — the single figure that answers "did the shelves hold more or
    /// less than the system thought".</summary>
    public decimal NetQuantityChange => TotalIncreaseQuantity - TotalDecreaseQuantity;

    public int AdjustmentCount => IncreasedCount + DecreasedCount;

    /// <summary>How many lines were rebased because stock moved mid-count. Non-zero means the
    /// applied adjustments differ from the variances the counter observed.</summary>
    public int RebasedItemCount { get; init; }
}
