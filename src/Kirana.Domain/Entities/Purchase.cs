using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// A finalized purchase from a supplier (PRD §28). <see cref="PurchaseNumber"/> is generated once
/// and never reused (mirrors <see cref="Sale.InvoiceNumber"/>). <see cref="AmountPaid"/> is the
/// payment made at purchase-entry time (if any) — later partial/full payments are separate
/// <see cref="SupplierPayment"/> rows against the supplier, not additional fields here.
/// </summary>
public class Purchase : Entity
{
    public string PurchaseNumber { get; set; } = string.Empty;
    public string? SupplierInvoiceNumber { get; set; }
    public DateTime PurchaseDateUtc { get; set; } = DateTime.UtcNow;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

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
