using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

public enum ReplenishmentStatus
{
    Healthy,
    AtReorderLevel,
    BelowReorderLevel,
    NotConfigured,
    InvalidConfiguration,
    OutOfStock,
}

public sealed class ReplenishmentQuery
{
    public string? SearchText { get; init; }
    public int? SupplierId { get; init; }
    public ReplenishmentStatus? Status { get; init; }
    public bool? Enabled { get; init; }
    public bool NeedsReorderOnly { get; init; } = true;
}

public sealed record ReplenishmentRecommendation(
    int ProductId,
    string ProductCode,
    string ProductName,
    UnitOfMeasure Unit,
    decimal CurrentStock,
    decimal ReorderLevel,
    decimal TargetStock,
    decimal OpenPurchaseOrderQuantity,
    decimal ProjectedStock,
    decimal SuggestedQuantity,
    int? PreferredSupplierId,
    string? PreferredSupplierName,
    decimal? EstimatedUnitCost,
    decimal? EstimatedOrderValue,
    ReplenishmentStatus Status,
    bool IsConfigured);

public sealed record ReplenishmentSummary(
    IReadOnlyList<ReplenishmentRecommendation> Items,
    int ProductsNeedingReorder,
    decimal TotalSuggestedUnits,
    decimal EstimatedOrderValue,
    int EstimatedValueUnavailableCount,
    int UnconfiguredLowStockProducts,
    DateTime CalculatedAtUtc);

