using Kirana.Domain.Common;

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
    public string? Notes { get; set; }
    public decimal CreditBalance { get; set; }
    public bool IsActive { get; set; } = true;
}
