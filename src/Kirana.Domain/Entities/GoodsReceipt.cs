using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>A non-posting record of goods physically received against a purchase order.
/// Inventory and supplier payable are posted only when the linked Purchase is finalized.</summary>
public sealed class GoodsReceipt : Entity
{
    public string GoodsReceiptNumber { get; set; } = string.Empty;
    public int PurchaseOrderId { get; set; }
    public PurchaseOrder PurchaseOrder { get; set; } = null!;
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string SupplierCodeSnapshot { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public GoodsReceiptStatus Status { get; set; } = GoodsReceiptStatus.Draft;
    public string? Notes { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public int? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public int? CancelledByUserId { get; set; }
    public User? CancelledByUser { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }
    public Purchase? Purchase { get; set; }

    public ICollection<GoodsReceiptItem> Items { get; set; } = new List<GoodsReceiptItem>();
}
