namespace Kirana.App.ViewModels;

/// <summary>Flattened, read-only customer list row. Financial values originate from the
/// permission-gated customer credit service; this type only supplies display-friendly values.</summary>
public sealed class CustomerRowViewModel
{
    public int Id { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? Gstin { get; init; }
    public string? Notes { get; init; }
    public decimal OutstandingBalance { get; init; }
    public bool IsActive { get; init; }
    public bool CanManageCustomers { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public DateTime? OldestOpenCreditDateUtc { get; init; }
    public DateTime? LastPurchaseDateUtc { get; init; }
    public DateTime? LastPaymentDateUtc { get; init; }
    public decimal LifetimePurchaseValue { get; init; }

    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.TrimStart()[0].ToString().ToUpperInvariant();
    public string PhoneText => string.IsNullOrWhiteSpace(Phone) ? "No mobile number" : Phone;
    public string LastPurchaseText => LastPurchaseDateUtc is { } date ? date.ToLocalTime().ToString("dd MMM yyyy") : "No purchases";
    public string LastPaymentText => LastPaymentDateUtc is { } date ? date.ToLocalTime().ToString("dd MMM yyyy") : "No payments";
    public bool IsPaid => OutstandingBalance <= 0m;
    public bool HasOutstanding => OutstandingBalance > 0m;
    public bool IsOverdue => OutstandingBalance > 0m && OldestOpenCreditDateUtc is { } date && date < DateTime.UtcNow.AddDays(-30);
    public bool IsOutstanding => OutstandingBalance > 0m && !IsOverdue;
    public bool CanReceivePayment => CanManageCustomers && HasOutstanding;
    public string CustomerTag => LifetimePurchaseValue >= 50_000m ? "VIP" : "Regular";
    public bool IsVip => CustomerTag == "VIP";
}
