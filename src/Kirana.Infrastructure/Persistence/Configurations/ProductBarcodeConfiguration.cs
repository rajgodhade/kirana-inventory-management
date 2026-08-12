using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class ProductBarcodeConfiguration : IEntityTypeConfiguration<ProductBarcode>
{
    public void Configure(EntityTypeBuilder<ProductBarcode> builder)
    {
        builder.Property(b => b.Value).IsRequired().HasMaxLength(BarcodeNormalizer.MaxBarcodeLength);
        builder.Property(b => b.NormalizedValue).IsRequired().HasMaxLength(BarcodeNormalizer.MaxBarcodeLength);
        builder.Property(b => b.Symbology).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.Notes).HasMaxLength(200);

        builder.HasOne(b => b.Product)
            .WithMany(p => p.Barcodes)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        // Store-wide uniqueness is on the NORMALIZED value, so "abc123" and "ABC123" can never both
        // exist. Deliberately unfiltered: a retired barcode still owns its value, otherwise
        // reactivating it could resurrect a duplicate, and a retired label still on a shelf could
        // start resolving to a different product.
        builder.HasIndex(b => b.NormalizedValue).IsUnique();

        builder.HasIndex(b => b.ProductId);

        // The POS hot path filters IsActive then equality-probes NormalizedValue; this column order
        // lets SQLite satisfy both from one index.
        builder.HasIndex(b => new { b.IsActive, b.NormalizedValue });

        // At most one primary per product, enforced by the database rather than by trusting the
        // service layer. Unique on ProductId alone, filtered to primary rows so the many
        // IsPrimary=0 rows don't collide. Deliberately NOT also filtered on IsActive: deactivating
        // a primary without demoting it keeps the slot, which is what the auto-promote logic in
        // BarcodeService relies on.
        builder.HasIndex(b => b.ProductId)
            .IsUnique()
            .HasFilter("\"IsPrimary\" = 1")
            .HasDatabaseName("IX_ProductBarcodes_ProductId_Primary");
    }
}
