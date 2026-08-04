namespace Kirana.Application.Purchasing;

/// <summary>One line of a purchase, as fed into <see cref="PurchasePricingCalculator"/>. Unlike
/// billing, the unit price here is always the negotiated purchase price supplied by the caller —
/// never read from <c>Product.PurchasePrice</c> — since that's exactly what this purchase is
/// meant to (re)establish going forward.</summary>
public sealed class PurchaseLine
{
    public required int ProductId { get; init; }
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public bool IsTaxInclusive { get; init; }
    public decimal GstRatePercent { get; init; }
    public decimal DiscountPercent { get; init; }
}
