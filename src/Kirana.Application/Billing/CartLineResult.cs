namespace Kirana.Application.Billing;

/// <summary>Computed amounts for one <see cref="CartLine"/>, after both the item-level discount
/// and its proportional share of the bill-level discount have been applied.</summary>
public sealed class CartLineResult
{
    public required CartLine Line { get; init; }
    public decimal GrossAmount { get; init; }
    public decimal DiscountAmount { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal GstAmount { get; init; }
    public decimal LineTotal { get; init; }
}
