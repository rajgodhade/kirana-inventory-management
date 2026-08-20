using Kirana.Domain.Common;
using Kirana.Domain.Taxation;

namespace Kirana.Domain.Entities;

/// <summary>
/// Customer master record (PRD §30-31). <see cref="CustomerCode"/> is the stable, auto-generated
/// identifier (e.g. "CUST-000001") and must never change after creation.
///
/// <see cref="CreditBalance"/> is a denormalized running total of everything this customer still
/// owes on Udhaar. It is maintained by <c>SaleService</c> (credit sales) and
/// <c>CustomerCreditService</c> (repayments); the authoritative reconstruction is always the sum of
/// <see cref="CustomerCredit.RemainingAmount"/> across the customer's credits, which the ledger
/// rebuilds from scratch on every request.
/// </summary>
public class Customer : Entity
{
    public string CustomerCode { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Gstin { get; set; }
    public string? StateCode { get; set; }
    public GstRegistrationType? GstRegistrationType { get; set; }
    public string? Notes { get; set; }
    public decimal CreditBalance { get; set; }
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// The price level this customer's bills should OPEN at (Phase 15B-4).
    ///
    /// <para><c>null</c> means "no preference" — the till behaves exactly as it always has and
    /// starts at Retail. That is deliberately distinct from an explicit
    /// <see cref="PriceLevel.Retail"/>, which records that someone decided this customer is a
    /// retail customer; both produce the same opening level today, but only one of them is a
    /// stated decision.</para>
    ///
    /// <para>A DEFAULT, not a lock, and never a pricing authority: it seeds the bill's level when a
    /// bill is still empty, after which the POS selector — and ultimately
    /// <c>CompleteSaleRequest.PriceLevel</c> — decides what is actually charged. Nothing in sale
    /// resolution ever reads this field.</para>
    /// </summary>
    public PriceLevel? DefaultPriceLevel { get; set; }
}
