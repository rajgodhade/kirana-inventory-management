namespace Kirana.Application.Billing;

/// <summary>One line of a cart, as fed into <see cref="CartPricingCalculator"/>. Pricing/GST
/// values are taken from the product at the moment of billing — never a stored snapshot,
/// since nothing is final until the sale completes.</summary>
public sealed class CartLine
{
    public required int ProductId { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required bool IsTaxInclusive { get; init; }
    public decimal GstRatePercent { get; init; }
    public decimal DiscountPercent { get; init; }
}
