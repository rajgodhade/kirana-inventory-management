using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        // House convention: enums persist by member NAME, so reordering PriceLevel can never
        // silently reinterpret existing rows as a different level.
        builder.Property(x => x.Level).HasConversion<string>().HasMaxLength(20);

        // 18,2 matches every other money column in the schema (Product.SellingPrice, Mrp,
        // PurchasePrice), so a price round-trips identically whichever store it came from.
        builder.Property(x => x.Price).HasPrecision(18, 2);

        // "One active price per product per level" is a database invariant, not a service
        // convention — UI validation alone is insufficient (§9). Filtered so a withdrawn
        // (IsActive = 0) row keeps its history without blocking a new active one.
        builder.HasIndex(x => new { x.ProductId, x.Level })
            .IsUnique()
            .HasFilter("\"IsActive\" = 1")
            .HasDatabaseName("IX_ProductPrices_ProductId_Level_Active");

        // Backs the common "all prices for this product" read.
        builder.HasIndex(x => x.ProductId);

        builder.HasOne(x => x.Product).WithMany(p => p.Prices)
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
    }
}
