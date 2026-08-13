using Kirana.Application.Abstractions;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Barcodes;

public sealed class BarcodeLookupService(IKiranaDbContext db) : IBarcodeLookupService
{
    public async Task<Product?> LookupAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var normalized = BarcodeNormalizer.Normalize(barcode);

        // Resolved in two steps rather than one Select(b => b.Product).Include(...): EF cannot apply
        // Include after a Select that projects through a navigation, and would throw at runtime.
        //
        // The first query still probes IX_ProductBarcodes_IsActive_NormalizedValue and returns only
        // an int, so the extra round trip costs a primary-key lookup — not a scan of Products.
        //
        // Both IsActive filters are required (Phase 13B): a retired barcode and a discontinued
        // product must each refuse to enter a cart.
        var productId = await db.ProductBarcodes
            .Where(b => b.NormalizedValue == normalized && b.IsActive && b.Product.IsActive)
            .Select(b => (int?)b.ProductId)
            .FirstOrDefaultAsync(cancellationToken);

        if (productId is null)
        {
            return null;
        }

        return await db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == productId.Value, cancellationToken);
    }

    public async Task<BarcodeLookupDiagnostic?> LookupDiagnosticAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return null;
        }

        var normalized = BarcodeNormalizer.Normalize(barcode);

        var match = await db.ProductBarcodes
            .Where(b => b.NormalizedValue == normalized)
            .Select(b => new
            {
                Barcode = b,
                Product = b.Product,
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (match is null)
        {
            return null;
        }

        // Loaded separately rather than via Include on the projection above: this path runs once per
        // manual scan-test, never in a billing loop, so clarity beats shaving a round trip.
        var product = await db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Inventory)
            .FirstAsync(p => p.Id == match.Barcode.ProductId, cancellationToken);

        return new BarcodeLookupDiagnostic(
            product,
            match.Barcode.Value,
            match.Barcode.Symbology,
            match.Barcode.IsPrimary,
            match.Barcode.IsActive,
            product.IsActive);
    }
}
