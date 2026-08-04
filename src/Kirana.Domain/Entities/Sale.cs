using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// A completed POS sale (PRD §19, §22). <see cref="InvoiceNumber"/> is generated once and never
/// reused. <see cref="CashierUserId"/> is nullable because normal billing needs no login (PRD
/// §4: "No management password is required for normal billing") — most sales will have no
/// authenticated user attached at all.
/// </summary>
public class Sale : Entity
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateTime SaleDateUtc { get; set; } = DateTime.UtcNow;

    public int? CustomerId { get; set; }
    public Customer? Customer { get; set; }

    public int? CashierUserId { get; set; }
    public User? CashierUser { get; set; }

    public decimal SubTotal { get; set; }
    public decimal ItemDiscountTotal { get; set; }
    public decimal BillDiscountPercent { get; set; }
    public decimal BillDiscountAmount { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOffAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public SaleStatus Status { get; set; } = SaleStatus.Completed;

    /// <summary>Who authorized a discount above the cashier's normal limit, if any (PRD §10).</summary>
    public int? DiscountAuthorizedByUserId { get; set; }
    public User? DiscountAuthorizedByUser { get; set; }

    /// <summary>Who authorized overriding one or more line's selling price away from the product's
    /// current price, if any.</summary>
    public int? PriceOverrideAuthorizedByUserId { get; set; }
    public User? PriceOverrideAuthorizedByUser { get; set; }

    public ICollection<SaleItem> Items { get; set; } = new List<SaleItem>();
    public ICollection<Payment> Payments { get; set; } = new List<Payment>();
}
