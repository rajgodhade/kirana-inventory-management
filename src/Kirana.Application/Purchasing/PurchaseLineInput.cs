namespace Kirana.Application.Purchasing;

/// <summary>One requested purchase line. GST rate/tax-inclusive flag always come from the
/// product's current master data (like billing) — only <see cref="UnitPrice"/> (the negotiated
/// purchase price) and discount are supplied fresh per purchase.</summary>
public sealed class PurchaseLineInput
{
    public required int ProductId { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public decimal DiscountPercent { get; init; }

    /// <summary>Batch/expiry data captured at purchase time (PRD §27) — only meaningful when the
    /// product tracks batches; ignored otherwise.</summary>
    public string? BatchNumber { get; init; }
    public DateOnly? ManufacturingDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
}
