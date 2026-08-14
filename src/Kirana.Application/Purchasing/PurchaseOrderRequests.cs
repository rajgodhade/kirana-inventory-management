using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

public sealed class PurchaseOrderLineInput
{
    public required int ProductId { get; init; }
    public required decimal OrderedQuantity { get; init; }
    public required decimal UnitCost { get; init; }
    public decimal DiscountPercent { get; init; }
    public PricingType? PricingType { get; init; }
}

public sealed class SavePurchaseOrderDraftRequest
{
    public required int SupplierId { get; init; }
    public required IReadOnlyList<PurchaseOrderLineInput> Lines { get; init; }
    public DateTime? OrderDateUtc { get; init; }
    public string? Notes { get; init; }
    public int? PerformedByUserId { get; init; }
}

public sealed class CancelPurchaseOrderRequest
{
    public required int PurchaseOrderId { get; init; }
    public required string Reason { get; init; }
    public int? PerformedByUserId { get; init; }
}

public enum PurchaseOrderSort
{
    Newest,
    Oldest,
    Number,
    Supplier,
    HighestTotal,
}

public sealed class PurchaseOrderSearchQuery
{
    public string? SearchText { get; init; }
    public PurchaseOrderStatus? Status { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public PurchaseOrderSort Sort { get; init; } = PurchaseOrderSort.Newest;
    public int MaxResults { get; init; } = 500;
}
