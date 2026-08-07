namespace Kirana.Application.Customers;

/// <summary>
/// Read-only operational projection for the Customer &amp; Udhaar list. It aggregates existing
/// customer, sales, credit, and repayment facts only; no accounting state is created or changed.
/// </summary>
public sealed class CustomerOverview
{
    public int Id { get; init; }
    public string CustomerCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? Gstin { get; init; }
    public string? Notes { get; init; }
    public bool IsActive { get; init; }
    public DateTime CreatedAtUtc { get; init; }
    public decimal OutstandingBalance { get; init; }
    public DateTime? OldestOpenCreditDateUtc { get; init; }
    public DateTime? LastPurchaseDateUtc { get; init; }
    public DateTime? LastPaymentDateUtc { get; init; }
    public decimal LifetimePurchaseValue { get; init; }
}
