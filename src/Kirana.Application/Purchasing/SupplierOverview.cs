namespace Kirana.Application.Purchasing;

/// <summary>
/// Read-only supplier list projection for operational screens. It deliberately combines only
/// existing supplier, purchase, and payment facts; it does not introduce new accounting state.
/// </summary>
public sealed class SupplierOverview
{
    public int Id { get; init; }
    public string SupplierCode { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string? ContactPerson { get; init; }
    public string? Phone { get; init; }
    public decimal OutstandingBalance { get; init; }
    public bool IsActive { get; init; }
    public DateTime? LastPurchaseDateUtc { get; init; }
    public DateTime? LastPaymentDateUtc { get; init; }
    public decimal TotalPurchases { get; init; }
}
