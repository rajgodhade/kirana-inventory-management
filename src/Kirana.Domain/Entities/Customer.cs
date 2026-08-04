using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Minimal customer record for POS selection and Udhaar/credit tracking (PRD §30-31). Full
/// ledger/purchase-history management is Phase 8 — this phase only needs enough to pick a
/// customer at billing time and track their running credit balance.
/// </summary>
public class Customer : Entity
{
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Gstin { get; set; }
    public decimal CreditBalance { get; set; }
    public bool IsActive { get; set; } = true;
}
