namespace Kirana.Application.Customers;

/// <summary>One customer's Udhaar position for the outstanding-summary screen (PRD §31).</summary>
public sealed class CustomerOutstandingSummary
{
    public int CustomerId { get; init; }
    public string CustomerCode { get; init; } = "";
    public string Name { get; init; } = "";
    public string? Phone { get; init; }
    public decimal OutstandingAmount { get; init; }
    public int OpenCreditCount { get; init; }
    public DateTime? OldestUnpaidDateUtc { get; init; }
}
