using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

/// <summary>Everything needed to finalize one purchase (PRD §28) — atomically creates the
/// Purchase/PurchaseItems, increases inventory, writes stock movements, creates/updates batches,
/// and (if <see cref="AmountPaid"/> is positive) records the initial supplier payment.</summary>
public sealed class CreatePurchaseRequest
{
    public required int SupplierId { get; init; }
    public string? SupplierInvoiceNumber { get; init; }
    public DateTime? PurchaseDateUtc { get; init; }
    public required IReadOnlyList<PurchaseLineInput> Lines { get; init; }

    public decimal AmountPaid { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public string? PaymentReferenceNumber { get; init; }

    public string? Notes { get; init; }
    public int? CreatedByUserId { get; init; }
    /// <summary>Optional non-posting source documents. Direct purchases leave both null.</summary>
    public int? GoodsReceiptId { get; init; }
    public int? PurchaseOrderId { get; init; }
}
