namespace Kirana.Application.Customers;

/// <summary>Input for editing a customer. Credit balance is never editable here — it only changes
/// through credit sales and repayments.</summary>
public sealed class UpdateCustomerRequest
{
    public required string Name { get; init; }
    public string? Phone { get; init; }
    public string? Address { get; init; }
    public string? Gstin { get; init; }
    public string? Notes { get; init; }
    public int? PerformedByUserId { get; init; }
}
