using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.Property(p => p.ProductCode).IsRequired().HasMaxLength(30);
        builder.HasIndex(p => p.ProductCode).IsUnique();

        // Sku is optional but must be unique when present (PRD §11, §13). Barcodes moved to their
        // own table in Phase 13B — see ProductBarcodeConfiguration.
        builder.Property(p => p.Sku).HasMaxLength(60);
        builder.HasIndex(p => p.Sku).IsUnique().HasFilter("\"Sku\" IS NOT NULL");

        builder.Property(p => p.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(p => p.Name);

        builder.Property(p => p.Unit).HasConversion<string>().HasMaxLength(20);

        // Optional purchase pack (Phase 13A) — null unless the product can be bought in bulk.
        builder.Property(p => p.PurchasePackUnit).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.PurchasePackSize).HasPrecision(18, 3);
        builder.Property(p => p.UnitDisplayText).HasMaxLength(40);

        builder.Property(p => p.PurchasePrice).HasPrecision(18, 2);
        builder.Property(p => p.Mrp).HasPrecision(18, 2);
        builder.Property(p => p.SellingPrice).HasPrecision(18, 2);
        builder.Property(p => p.WholesalePrice).HasPrecision(18, 2);
        builder.Property(p => p.DefaultDiscountPercent).HasPrecision(5, 2);
        builder.Property(p => p.GstRatePercent).HasPrecision(5, 2);
        builder.Property(p => p.PricingType).HasConversion<string>().HasMaxLength(20).HasDefaultValue(PricingType.Inclusive);
        builder.Property(p => p.MinimumStock).HasPrecision(18, 3);
        builder.Property(p => p.ReorderQuantity).HasPrecision(18, 3);
        builder.Property(p => p.ReplenishmentEnabled).HasDefaultValue(false);

        builder.Property(p => p.HsnCode).HasMaxLength(20);

        builder.HasOne(p => p.Category)
            .WithMany(c => c.Products)
            .HasForeignKey(p => p.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.Brand)
            .WithMany(b => b.Products)
            .HasForeignKey(p => p.BrandId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(p => p.PreferredSupplier)
            .WithMany(s => s.PreferredForProducts)
            .HasForeignKey(p => p.PreferredSupplierId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(p => p.CategoryId);
        builder.HasIndex(p => p.BrandId);
        builder.HasIndex(p => p.IsActive);
        builder.HasIndex(p => p.PreferredSupplierId);
        builder.HasIndex(p => p.ReplenishmentEnabled);
    }
}
