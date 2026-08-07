namespace Kirana.App.ViewModels;

/// <summary>Flattened row for the Suppliers list (PRD §28). No <c>required</c> members — see the
/// Phase 5 note on avoiding required members on types reachable from a bound ViewModel.</summary>
public sealed class SupplierRowViewModel
{
    public int Id { get; init; }
    public string SupplierCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public decimal OutstandingBalance { get; init; }
    public bool IsActive { get; init; }
    public DateTime? LastPurchaseDateUtc { get; init; }
    public DateTime? LastPaymentDateUtc { get; init; }
    public decimal TotalPurchases { get; init; }

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.TrimStart()[0].ToString().ToUpperInvariant();
    public string ContactText => string.IsNullOrWhiteSpace(ContactPerson) ? "No contact person" : ContactPerson;
    public string PhoneText => string.IsNullOrWhiteSpace(Phone) ? "No phone" : Phone;
    public string LastPurchaseText => LastPurchaseDateUtc is { } date ? date.ToLocalTime().ToString("dd MMM yyyy") : "No purchases";
    public string LastPaymentText => LastPaymentDateUtc is { } date ? date.ToLocalTime().ToString("dd MMM yyyy") : "No payments";
    public bool IsPaid => OutstandingBalance <= 0m;
    public bool HasOutstanding => OutstandingBalance > 0m;
    public bool IsOverdue => OutstandingBalance > 0m && LastPurchaseDateUtc is { } date && date < DateTime.UtcNow.AddDays(-30);
    public bool IsOutstanding => OutstandingBalance > 0m && !IsOverdue;
}
