using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Barcodes;

public sealed class BarcodeLookupService(IKiranaDbContext db) : IBarcodeLookupService
{
    public Task<Product?> LookupAsync(string barcode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(barcode))
        {
            return Task.FromResult<Product?>(null);
        }

        var trimmed = barcode.Trim();
        return db.Products
            .Include(p => p.Category)
            .Include(p => p.Brand)
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Barcode == trimmed, cancellationToken);
    }
}
