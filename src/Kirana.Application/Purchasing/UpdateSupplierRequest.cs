namespace Kirana.Application.Purchasing;

/// <summary>Input for editing an existing supplier. Outstanding balance is never editable here —
/// it only changes through purchase/payment writes.</summary>
public sealed class UpdateSupplierRequest
{
    public required string Name { get; init; }
    public string? Gstin { get; init; }
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public string? Email { get; init; }
    public string? Address { get; init; }
    public int? PerformedByUserId { get; init; }
}
