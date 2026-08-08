namespace Kirana.Application.Billing;

using Kirana.Domain.Entities;

/// <summary>One line of a cart, as fed into <see cref="CartPricingCalculator"/>. Pricing/GST
/// values are taken from the product at the moment of billing — never a stored snapshot,
/// since nothing is final until the sale completes.</summary>
public sealed class CartLine
{
    private PricingType _pricingType = PricingType.Inclusive;
    public required int ProductId { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public PricingType PricingType { get => _pricingType; init => _pricingType = value; }
    public bool IsTaxInclusive { get => _pricingType == PricingType.Inclusive; init => _pricingType = value ? PricingType.Inclusive : PricingType.Exclusive; }
    public decimal GstRatePercent { get; init; }
    public decimal DiscountPercent { get; init; }
    public decimal PromotionBeforeTaxDiscountAmount { get; init; }
    public decimal PromotionAfterTaxDiscountAmount { get; init; }
}
