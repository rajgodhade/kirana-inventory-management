using Kirana.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Products;

/// <inheritdoc cref="IProductPriceResolver"/>
public sealed class ProductPriceResolver(IKiranaDbContext db) : IProductPriceResolver
{
    public async Task<PriceResolution> ResolveAsync(
        int productId, PricingContext context, CancellationToken cancellationToken = default)
    {
        var level = context.PriceLevel;

        // One query, one round trip: the product's active flag and the prices at the requested level,
        // projected to just what is needed. No Include, no Product graph, and AsNoTracking so the
        // resolver never returns an entity another context has already modified in memory — the
        // identity-map staleness that bit Phase 13C's stock check.
        var snapshot = await db.Products
            .AsNoTracking()
            .Where(p => p.Id == productId)
            .Select(p => new
            {
                p.IsActive,
                // A list rather than FirstOrDefault so an impossible second active row is DETECTED
                // rather than silently resolved. See the count check below.
                Prices = p.Prices
                    .Where(price => price.Level == level && price.IsActive)
                    .Select(price => price.Price)
                    .ToList(),
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (snapshot is null)
        {
            throw new InvalidOperationException($"Product #{productId} was not found.");
        }

        // Inactive products are excluded the same way BarcodeLookupService excludes them from POS
        // lookups, rather than throwing the way SaleService does when one reaches a bill. Selling an
        // inactive product stays SaleService's rule to enforce; this service only reports that a
        // discontinued product has no current price.
        if (!snapshot.IsActive)
        {
            return PriceResolution.Unavailable(productId, level, PriceUnavailableReason.ProductInactive);
        }

        if (snapshot.Prices.Count > 1)
        {
            // Phase 15A makes this unreachable through the application: a filtered unique index on
            // (ProductId, Level) WHERE IsActive = 1 rejects a second active row. If it happens
            // anyway the data is corrupt, and picking one arbitrarily would mean billing a customer
            // a price chosen by row order. Failing loudly is the only honest option.
            throw new InvalidOperationException(
                $"Product #{productId} has {snapshot.Prices.Count} active {level} prices; expected at most one.");
        }

        // Count == 0 covers both "level never configured" and "the row exists but was withdrawn",
        // because the query already filters IsActive. A retired price is not a current price.
        if (snapshot.Prices.Count == 0)
        {
            return PriceResolution.Unavailable(productId, level, PriceUnavailableReason.LevelNotConfigured);
        }

        // Returned exactly as stored. Rounding happened once, on the way in.
        return PriceResolution.Resolved(productId, level, snapshot.Prices[0]);
    }
}
