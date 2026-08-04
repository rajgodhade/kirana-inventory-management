namespace Kirana.Application.Billing;

/// <summary>One cart line as submitted to <see cref="ISaleService.CompleteSaleAsync"/>. GST is
/// always pulled from the current <c>Product</c> record at completion time, never supplied by the
/// caller. Price is too, <em>unless</em> <see cref="UnitPriceOverride"/> is set — and even then,
/// <see cref="SaleService"/> re-verifies the request carries a valid manager authorization for
/// <see cref="Domain.Entities.PermissionKeys.PricingChangeSellingPrice"/> before honoring it, so a
/// client still can't silently smuggle in an arbitrary price.</summary>
public sealed class SaleLineInput
{
    public required int ProductId { get; init; }
    public required decimal Quantity { get; init; }
    public decimal DiscountPercent { get; init; }

    /// <summary>A per-bill selling-price override for this line, applying only to this sale — the
    /// product's own <c>SellingPrice</c> is never touched. Null (the default) means "use the
    /// product's current selling price."</summary>
    public decimal? UnitPriceOverride { get; init; }
}
