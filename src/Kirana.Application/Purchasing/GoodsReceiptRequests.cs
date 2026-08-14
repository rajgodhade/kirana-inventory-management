using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

public sealed class GoodsReceiptLineInput
{
    public required int PurchaseOrderItemId { get; init; }
    public required decimal ReceivedQuantity { get; init; }
    public string? Barcode { get; init; }
    public string? Notes { get; init; }
}

public sealed class CreateGoodsReceiptDraftRequest
{
    public required int PurchaseOrderId { get; init; }
    public required IReadOnlyList<GoodsReceiptLineInput> Lines { get; init; }
    public DateTime? ReceivedAtUtc { get; init; }
    public string? Notes { get; init; }
    public int? PerformedByUserId { get; init; }
}

public sealed class CancelGoodsReceiptRequest
{
    public required int GoodsReceiptId { get; init; }
    public required string Reason { get; init; }
    public int? PerformedByUserId { get; init; }
}

public sealed class GoodsReceiptSearchQuery
{
    public string? SearchText { get; init; }
    public int? SupplierId { get; init; }
    public GoodsReceiptStatus? Status { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public bool OldestFirst { get; init; }
    public int MaxResults { get; init; } = 500;
}

public sealed record PurchaseOrderReceiptLine(
    int PurchaseOrderItemId,
    int ProductId,
    string ProductName,
    string ProductCode,
    UnitOfMeasure Unit,
    decimal OrderedQuantity,
    decimal PreviouslyReceivedQuantity,
    decimal RemainingQuantity,
    decimal ExpectedUnitCost,
    decimal ExpectedDiscountPercent,
    PricingType PricingType);

public sealed record PurchaseOrderReceiptPreview(
    int PurchaseOrderId,
    string PurchaseOrderNumber,
    int SupplierId,
    string SupplierName,
    DateTime OrderDateUtc,
    PurchaseOrderStatus Status,
    IReadOnlyList<PurchaseOrderReceiptLine> Lines);

public sealed record GoodsReceiptPurchasePrefill(
    int GoodsReceiptId,
    string GoodsReceiptNumber,
    int PurchaseOrderId,
    string PurchaseOrderNumber,
    int SupplierId,
    IReadOnlyList<PurchaseLineInput> Lines);
