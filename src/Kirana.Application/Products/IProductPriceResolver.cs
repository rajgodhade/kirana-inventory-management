namespace Kirana.Application.Products;

/// <summary>
/// Answers "what is this product's price under this context?" (Phase 15B-1).
///
/// <para>Reads from <see cref="Kirana.Domain.Entities.ProductPrice"/> — the authoritative store —
/// never from the <c>Product.SellingPrice</c> / <c>Product.WholesalePrice</c> projection columns.
/// Resolving through the projections would branch per level and quietly recreate the two pricing
/// systems Phase 15A merged.</para>
///
/// <para><b>Read-only and side-effect free.</b> Resolving a price writes nothing: no entity change,
/// no audit row, no stock movement. Calling it twice is the same as calling it once, which is what
/// lets a cart re-price itself freely.</para>
///
/// <para><b>No fallback policy.</b> Asking for a level a product does not configure yields
/// <see cref="PriceUnavailableReason.LevelNotConfigured"/>, never a silent substitution of another
/// level. Whether wholesale should ever fall back to retail is a policy decision, and inventing it
/// here would bury it in a lookup.</para>
///
/// <para><b>POS does not use this yet.</b> Billing still reads <c>Product.SellingPrice</c>
/// unchanged; wiring the till through the resolver is a later 15B task.</para>
/// </summary>
public interface IProductPriceResolver
{
    /// <summary>
    /// Resolves one product's price for the given context.
    ///
    /// <para>Throws <see cref="InvalidOperationException"/> when the product does not exist, matching
    /// how <c>SaleService</c> and <c>ProductPricingService</c> treat an unknown product id — an
    /// unknown id is a caller bug, not a pricing outcome. A product that exists but has no
    /// applicable price returns an unavailable result instead, because that IS a pricing outcome.</para>
    /// </summary>
    Task<PriceResolution> ResolveAsync(
        int productId, PricingContext context, CancellationToken cancellationToken = default);
}
