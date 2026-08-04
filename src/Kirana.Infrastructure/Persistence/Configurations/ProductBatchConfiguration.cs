using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class ProductBatchConfiguration : IEntityTypeConfiguration<ProductBatch>
{
    public void Configure(EntityTypeBuilder<ProductBatch> builder)
    {
        builder.Property(b => b.BatchNumber).IsRequired().HasMaxLength(100);
        builder.Property(b => b.Quantity).HasPrecision(18, 3);
        builder.Property(b => b.PurchasePrice).HasPrecision(18, 2);
        builder.Property(b => b.SellingPrice).HasPrecision(18, 2);

        builder.HasOne(b => b.Product)
            .WithMany(p => p.Batches)
            .HasForeignKey(b => b.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(b => b.ProductId);
        builder.HasIndex(b => b.ExpiryDate);
    }
}
