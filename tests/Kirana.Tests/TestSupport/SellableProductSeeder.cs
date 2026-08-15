using Kirana.Domain.Entities;

namespace Kirana.Tests.TestSupport;

/// <summary>
/// Gives a hand-built <see cref="Product"/> the active Retail <see cref="ProductPrice"/> that every
/// real product has.
///
/// <para>Since Phase 15A, <c>ProductPrice</c> is the authoritative selling-price store:
/// <c>ProductService.CreateAsync</c> always stages a Retail row, the import does the same, and the
/// 15A migration backfilled one for every product that already existed. A <c>Product</c> carrying
/// only a <c>SellingPrice</c> column is therefore a state the application cannot produce.</para>
///
/// <para>That did not matter while POS read the projection column. From Phase 15B-2 the till
/// resolves through <see cref="Kirana.Application.Products.IProductPriceResolver"/> and refuses to
/// sell an unpriced product, so tests that build products by hand have to model the real invariant.
/// This is the one line that does it — deliberately explicit at each seeding site rather than
/// fabricated behind the fixture, so a reader can see the product genuinely has a price.</para>
/// </summary>
public static class SellableProductSeeder
{
    /// <summary>Adds the active Retail price, defaulting to the product's own
    /// <c>SellingPrice</c> so the two stores agree exactly as the pricing service keeps them.</summary>
    public static Product WithRetailPrice(this Product product, decimal? price = null)
    {
        product.Prices.Add(new ProductPrice
        {
            Level = PriceLevel.Retail,
            Price = price ?? product.SellingPrice,
            IsActive = true,
        });

        return product;
    }

    /// <summary>Adds an active Wholesale price alongside retail, for tests that need both levels.</summary>
    public static Product WithWholesalePrice(this Product product, decimal price)
    {
        product.Prices.Add(new ProductPrice
        {
            Level = PriceLevel.Wholesale,
            Price = price,
            IsActive = true,
        });

        return product;
    }
}
