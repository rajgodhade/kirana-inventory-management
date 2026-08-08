namespace Kirana.Application.Purchasing;

using Kirana.Domain.Entities;

/// <summary>One line of a purchase, as fed into <see cref="PurchasePricingCalculator"/>. Unlike
/// billing, the unit price here is always the negotiated purchase price supplied by the caller —
/// never read from <c>Product.PurchasePrice</c> — since that's exactly what this purchase is
/// meant to (re)establish going forward.</summary>
public sealed class PurchaseLine
{
    private PricingType _pricingType = PricingType.Inclusive;
    public required int ProductId { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public PricingType PricingType { get => _pricingType; init => _pricingType = value; }
    public bool IsTaxInclusive { get => _pricingType == PricingType.Inclusive; init => _pricingType = value ? PricingType.Inclusive : PricingType.Exclusive; }
    public decimal GstRatePercent { get; init; }
    public decimal DiscountPercent { get; init; }
}
