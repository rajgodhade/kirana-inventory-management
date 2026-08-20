using Kirana.Domain.Common;
using Kirana.Domain.Taxation;

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

    /// <summary>
    /// GST/legal identity captured atomically when this sale was completed. A null capture time
    /// identifies a legacy sale created before Phase 18A-2; its identity must never be guessed or
    /// backfilled from today's master records.
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

    public string? CustomerNameSnapshot { get; set; }
    public string? CustomerPhoneSnapshot { get; set; }
    public string? CustomerGstinSnapshot { get; set; }
    public string? CustomerStateCodeSnapshot { get; set; }
    public string? CustomerStateNameSnapshot { get; set; }
    public GstRegistrationType? CustomerGstRegistrationTypeSnapshot { get; set; }
    public string? CustomerAddressSnapshot { get; set; }

    public int? CashierUserId { get; set; }
    public User? CashierUser { get; set; }

    public decimal SubTotal { get; set; }
    public decimal ItemDiscountTotal { get; set; }
    public decimal PromotionDiscountTotal { get; set; }
    public decimal BillDiscountPercent { get; set; }
    public decimal BillDiscountAmount { get; set; }
    public decimal TaxableTotal { get; set; }
    public decimal TaxTotal { get; set; }
    public decimal RoundOffAmount { get; set; }
    public decimal GrandTotal { get; set; }

    public SaleStatus Status { get; set; } = SaleStatus.Completed;

    /// <summary>
    /// The price level this bill was actually sold at (Phase 15B-5). Bill-wide, because that is how
    /// the level is chosen — there is deliberately no per-line level, and a sale therefore has
    /// exactly one.
    ///
    /// <para>Historical metadata, written once at completion and never changed. It records the
    /// pricing CONTEXT; <see cref="SaleItem.UnitPriceSnapshot"/> remains authoritative for what each
    /// line actually cost. Neither is ever recomputed from today's prices or from the customer's
    /// current preference.</para>
    ///
    /// <para>Sales created before this column existed are classified <see cref="PriceLevel.Retail"/>
    /// by the migration. That is a labelling policy, not evidence: their real context was never
    /// stored, and it cannot be reconstructed from prices, totals or the customer.</para>
    /// </summary>
    public PriceLevel PriceLevel { get; set; } = PriceLevel.Retail;

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
