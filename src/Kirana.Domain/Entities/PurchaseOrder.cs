using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>A non-posting procurement commitment. It never changes inventory, supplier balances,
/// purchase accounting, or product costs; receipt posting belongs to the future GRN workflow.</summary>
public class PurchaseOrder : Entity
{
    public string PurchaseOrderNumber { get; set; } = string.Empty;
    public DateTime OrderDateUtc { get; set; } = DateTime.UtcNow;
    public PurchaseOrderStatus Status { get; set; } = PurchaseOrderStatus.Draft;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;
    public string SupplierNameSnapshot { get; set; } = string.Empty;
    public string SupplierCodeSnapshot { get; set; } = string.Empty;
    public string? SupplierContactSnapshot { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }
    public int? SubmittedByUserId { get; set; }
    public User? SubmittedByUser { get; set; }
    public DateTime? SubmittedAtUtc { get; set; }
    public int? CancelledByUserId { get; set; }
    public User? CancelledByUser { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancellationReason { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOffAmount { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Notes { get; set; }

    public ICollection<PurchaseOrderItem> Items { get; set; } = new List<PurchaseOrderItem>();
    public ICollection<GoodsReceipt> GoodsReceipts { get; set; } = new List<GoodsReceipt>();
}
