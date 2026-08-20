using Kirana.Domain.Common;
using Kirana.Domain.Taxation;

namespace Kirana.Domain.Entities;

/// <summary>
/// A finalized purchase from a supplier (PRD §28). <see cref="PurchaseNumber"/> is generated once
/// and never reused (mirrors <see cref="Sale.InvoiceNumber"/>). <see cref="AmountPaid"/> is the
/// payment made at purchase-entry time (if any) — later partial/full payments are separate
/// <see cref="SupplierPayment"/> rows against the supplier, not additional fields here.
/// </summary>
public class Purchase : Entity
{
    public int? GoodsReceiptId { get; set; }
    public GoodsReceipt? GoodsReceipt { get; set; }
    public int? PurchaseOrderId { get; set; }
    public PurchaseOrder? PurchaseOrder { get; set; }
    public string PurchaseNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime PurchaseDateUtc { get; set; } = DateTime.UtcNow;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    /// <summary>
    /// GST/legal identity captured atomically when this purchase was finalized. Null means this
    /// is a legacy purchase for which no historical identity evidence exists.
    /// </summary>
    public DateTime? GstIdentitySnapshotCapturedAtUtc { get; set; }
    public string? StoreTradeNameSnapshot { get; set; }
    public string? StoreLegalNameSnapshot { get; set; }
    public string? StoreGstinSnapshot { get; set; }
    public string? StoreStateCodeSnapshot { get; set; }
    public string? StoreStateNameSnapshot { get; set; }
    public GstRegistrationType? StoreGstRegistrationTypeSnapshot { get; set; }
    public string? StoreAddressSnapshot { get; set; }
    public string? StoreCitySnapshot { get; set; }
    public string? StorePinCodeSnapshot { get; set; }
    public string? StoreContactNumberSnapshot { get; set; }

    public string? SupplierNameSnapshot { get; set; }
    public string? SupplierCodeSnapshot { get; set; }
    public string? SupplierGstinSnapshot { get; set; }
    public string? SupplierStateCodeSnapshot { get; set; }
    public string? SupplierStateNameSnapshot { get; set; }
    public GstRegistrationType? SupplierGstRegistrationTypeSnapshot { get; set; }
    public string? SupplierAddressSnapshot { get; set; }

    public int? CreatedByUserId { get; set; }
    public User? CreatedByUser { get; set; }

    public decimal SubTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOffAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public decimal AmountPaid { get; set; }
    public decimal OutstandingAmount { get; set; }
    public PaymentMethod? PaymentMethod { get; set; }

    public string? Notes { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Completed;

    public ICollection<PurchaseItem> Items { get; set; } = new List<PurchaseItem>();
    public ICollection<SupplierPayment> Payments { get; set; } = new List<SupplierPayment>();
}
