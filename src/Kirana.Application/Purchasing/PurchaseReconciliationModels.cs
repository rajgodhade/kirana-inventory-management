using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

[Flags]
public enum PurchaseReconciliationFlags
{
    None = 0,
    FullyReconciled = 1 << 0,
    AwaitingReceipt = 1 << 1,
    PartiallyReceived = 1 << 2,
    AwaitingPurchase = 1 << 3,
    PendingPurchase = 1 << 4,
    QuantityMismatch = 1 << 5,
    PriceMismatch = 1 << 6,
    TaxMismatch = 1 << 7,
    OverInvoiced = 1 << 8,
    OverReceived = 1 << 9,
    Exception = 1 << 10,
}

public enum PurchaseReconciliationFilter
{
    All,
    FullyReconciled,
    PendingReceipt,
    PendingPurchase,
    QuantityMismatch,
    PriceMismatch,
    TaxMismatch,
    Exceptions,
}

public enum PurchaseReconciliationSort
{
    Newest,
    Oldest,
    Supplier,
    HighestVariance,
}

public sealed class PurchaseReconciliationQuery
{
    public int? PurchaseOrderId { get; init; }
    public string? SearchText { get; init; }
    public int? SupplierId { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public PurchaseReconciliationFilter Filter { get; init; }
    public PurchaseReconciliationSort Sort { get; init; }
    public int MaxResults { get; init; } = 500;
}

public sealed record PurchaseReconciliationDocument(
    int Id,
    string Number,
    DateTime DateUtc,
    string Status,
    decimal Quantity);

public sealed record PurchaseReconciliationLine(
    int PurchaseOrderItemId,
    int ProductId,
    string ProductName,
    string ProductCode,
    string? Sku,
    string Unit,
    decimal OrderedQuantity,
    decimal ReceivedQuantity,
    decimal PurchasedQuantity,
    decimal PendingReceiptQuantity,
    decimal PendingInvoiceQuantity,
    decimal OverReceivedQuantity,
    decimal OverInvoicedQuantity,
    decimal ExpectedUnitCost,
    decimal? ActualUnitCost,
    decimal? UnitCostVariance,
    decimal? UnitCostVariancePercent,
    decimal ExpectedTotal,
    decimal ActualTotal,
    decimal TotalVariance,
    decimal ExpectedDiscount,
    decimal ActualDiscount,
    decimal DiscountVariance,
    decimal ExpectedTax,
    decimal ActualTax,
    decimal TaxVariance,
    PurchaseReconciliationFlags Flags);

public sealed class PurchaseReconciliationRecord
{
    public required int PurchaseOrderId { get; init; }
    public required string PurchaseOrderNumber { get; init; }
    public required int SupplierId { get; init; }
    public required string SupplierName { get; init; }
    public required string SupplierCode { get; init; }
    public required DateTime OrderDateUtc { get; init; }
    public required PurchaseOrderStatus PurchaseOrderStatus { get; init; }
    public required DateTime CalculatedAtUtc { get; init; }
    public required IReadOnlyList<PurchaseReconciliationLine> Lines { get; init; }
    public required IReadOnlyList<PurchaseReconciliationDocument> GoodsReceipts { get; init; }
    public required IReadOnlyList<PurchaseReconciliationDocument> Purchases { get; init; }
    public required PurchaseReconciliationFlags Flags { get; init; }
    public decimal OrderedQuantity => Lines.Sum(x => x.OrderedQuantity);
    public decimal ReceivedQuantity => Lines.Sum(x => x.ReceivedQuantity);
    public decimal PurchasedQuantity => Lines.Sum(x => x.PurchasedQuantity);
    public decimal PendingReceiptQuantity => Lines.Sum(x => x.PendingReceiptQuantity);
    public decimal PendingInvoiceQuantity => Lines.Sum(x => x.PendingInvoiceQuantity);
    public decimal ExpectedValue => Lines.Sum(x => x.ExpectedTotal);
    public decimal ActualValue { get; init; }
    public decimal TotalVariance => ActualValue - ExpectedValue;
    public decimal ExpectedTax => Lines.Sum(x => x.ExpectedTax);
    public decimal ActualTax => Lines.Sum(x => x.ActualTax);
    public decimal TaxVariance => ActualTax - ExpectedTax;
    public decimal ExpectedDiscount => Lines.Sum(x => x.ExpectedDiscount);
    public decimal ActualDiscount => Lines.Sum(x => x.ActualDiscount);
    public decimal DiscountVariance => ActualDiscount - ExpectedDiscount;
    public bool Has(PurchaseReconciliationFlags flag) => (Flags & flag) != 0;
}

public sealed record PurchaseReconciliationMetrics(
    int TotalPurchaseOrders,
    int FullyReconciled,
    int PendingReceipt,
    int PendingPurchase,
    int QuantityExceptions,
    int PriceExceptions,
    int TaxExceptions,
    int Exceptions,
    decimal ExpectedPurchaseValue,
    decimal ActualPurchaseValue,
    decimal TotalVariance);

public sealed record PurchaseReconciliationResult(
    IReadOnlyList<PurchaseReconciliationRecord> Records,
    PurchaseReconciliationMetrics Metrics,
    DateTime CalculatedAtUtc);

