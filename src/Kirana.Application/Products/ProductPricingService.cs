using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Products;

public sealed class ProductPricingService(
    IKiranaDbContext db,
    IAuditLogger auditLogger,
    IPermissionEnforcer permissionEnforcer) : IProductPricingService
{
    /// <summary>Matches the 18,2 precision of every money column, so the stored price and the
    /// value a caller passed agree exactly rather than leaving a third-decimal remainder.</summary>
    private const int MoneyScale = 2;

    // ---- Reads (never audited) ----

    public async Task<decimal?> GetPriceAsync(
        int productId, PriceLevel level, CancellationToken cancellationToken = default) =>
        await db.ProductPrices
            .AsNoTracking()
            .Where(p => p.ProductId == productId && p.Level == level && p.IsActive)
            .Select(p => (decimal?)p.Price)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<ProductPrice>> GetPricesAsync(
        int productId, CancellationToken cancellationToken = default) =>
        await db.ProductPrices
            .AsNoTracking()
            .Where(p => p.ProductId == productId && p.IsActive)
            .OrderBy(p => p.Level)
            .ToListAsync(cancellationToken);

    // ---- Validation ----

    public void ValidatePrice(PriceLevel level, decimal price)
    {
        // Only the rule §7/§8 asks for. Deliberately NOT enforcing "wholesale < retail": some
        // shops price unusually on purpose, and advanced pricing rules belong to a later phase.
        if (price < 0m)
        {
            throw new ArgumentException(
                $"{level.ToDisplayText()} price cannot be negative.", nameof(price));
        }
    }

    // ---- The write ----

    public async Task<ProductPrice> SetPriceAsync(
        int productId, PriceLevel level, decimal price, int? performedByUserId,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(
            performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        ValidatePrice(level, price);
        var rounded = Round(price);

        var product = await db.Products
            .Include(p => p.Prices)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        // Same staging every other caller uses, so the arithmetic and projection have exactly one
        // implementation; this method only adds the transaction and the audit around it.
        var change = StagePrice(product, level, rounded);

        // The ProductPrice row and its projection column already commit together (one SaveChanges),
        // but the audit goes through a separate service — so the write is wrapped, exactly as
        // StockCountService and InventoryAdjustmentService do, rather than leaving a price changed
        // with no record of who changed it. Standalone price edits get this; ProductService's own
        // create/update keep their existing save-then-audit convention unchanged.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);

            // Only a real change is audited — re-saving the same number is not an event, and
            // logging it would bury genuine price changes in noise.
            if (change is not null)
            {
                await RecordPriceChangeAsync(product, change, performedByUserId, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }

        return product.Prices.First(p => p.Level == level && p.IsActive);
    }

    /// <summary>Writes the per-level audit entry for a staged change. Shared so every caller
    /// produces an identically shaped record.</summary>
    public async Task RecordPriceChangeAsync(
        Product product, PriceChange change, int? performedByUserId, CancellationToken cancellationToken = default) =>
        await auditLogger.RecordAsync(
            performedByUserId,
            change.NewPrice is null ? "PriceRemoved" : "PriceChanged",
            nameof(Product),
            product.Id.ToString(),
            previousValue: change.PreviousPrice?.ToString("0.00"),
            newValue: change.NewPrice?.ToString("0.00"),
            reason: $"{change.Level.ToDisplayText()} price for {product.ProductCode} {product.Name}",
            cancellationToken: cancellationToken);

    public PriceChange? StagePrice(Product product, PriceLevel level, decimal? price)
    {
        if (price is null && level.IsRequired())
        {
            throw new ArgumentException(
                $"{level.ToDisplayText()} price is required and cannot be removed.", nameof(price));
        }

        decimal? rounded = null;
        if (price is { } value)
        {
            // Validated here too, not just on the async entry points: this is the shared path every
            // caller funnels through, so it is the one place that can guarantee no negative price
            // ever reaches the database.
            ValidatePrice(level, value);
            rounded = Round(value);
        }

        var existing = product.Prices.FirstOrDefault(p => p.Level == level && p.IsActive);
        var previousPrice = existing?.Price;

        if (previousPrice == rounded)
        {
            return null; // Nothing to write, nothing to audit.
        }

        if (rounded is { } newPrice)
        {
            if (existing is null)
            {
                // Staged through the navigation rather than a ProductId, because on creation the
                // identity value does not exist until SaveChanges — same pattern as barcodes.
                existing = new ProductPrice { Product = product, Level = level, Price = newPrice, IsActive = true };
                db.ProductPrices.Add(existing);
                product.Prices.Add(existing);
            }
            else
            {
                // Updated in place: ProductPrice holds the CURRENT price per level, not a history.
                // What changed is recorded in the audit trail, and historical transactions keep
                // their own snapshots regardless.
                existing.Price = newPrice;
                existing.UpdatedAtUtc = DateTime.UtcNow;
            }
        }
        else if (existing is not null)
        {
            // Deactivated rather than deleted, so the row that existed stays visible to anyone
            // reconstructing what this product used to be priced at.
            existing.IsActive = false;
            existing.UpdatedAtUtc = DateTime.UtcNow;
        }

        ApplyProjection(product, level, rounded);
        product.UpdatedAtUtc = DateTime.UtcNow;

        return new PriceChange(level, previousPrice, rounded);
    }

    public async Task RemovePriceAsync(
        int productId, PriceLevel level, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(
            performedByUserId, PermissionKeys.ProductsEdit, cancellationToken);

        if (level.IsRequired())
        {
            throw new InvalidOperationException(
                $"{level.ToDisplayText()} price cannot be removed — every product must have one.");
        }

        var product = await db.Products
            .Include(p => p.Prices)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        var change = StagePrice(product, level, null);
        if (change is null)
        {
            return; // Nothing configured: removing it is a no-op, not an error.
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await RecordPriceChangeAsync(product, change, performedByUserId, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ---- Projection ----

    /// <summary>
    /// Mirrors a level onto the legacy Product column so POS, reports and exports keep reading what
    /// they always read. Written in the SAME SaveChanges as the ProductPrice row, so the two cannot
    /// diverge — this is what keeps a single write path rather than two competing stores.
    ///
    /// <para>Phase 15B retires this once POS resolves prices through the service.</para>
    /// </summary>
    private static void ApplyProjection(Product product, PriceLevel level, decimal? price)
    {
        switch (level)
        {
            case PriceLevel.Retail:
                // Retail is required, so a null here would mean "no shelf price", which
                // RemovePriceAsync already refuses. Guarded anyway rather than writing a silent 0.
                if (price is { } retail)
                {
                    product.SellingPrice = retail;
                }

                break;

            case PriceLevel.Wholesale:
                product.WholesalePrice = price;
                break;
        }
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, MoneyScale, MidpointRounding.AwayFromZero);
}
