namespace Kirana.Application.Purchasing;

/// <summary>Computed amounts for one <see cref="PurchaseLine"/>, after its item-level discount
/// and GST split.</summary>
public sealed class PurchaseLineResult
{
    public required PurchaseLine Line { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal GstAmount { get; init; }
    public decimal LineTotal { get; init; }
}
