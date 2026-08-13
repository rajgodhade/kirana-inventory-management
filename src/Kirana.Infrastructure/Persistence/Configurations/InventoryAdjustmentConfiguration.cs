using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class InventoryAdjustmentConfiguration : IEntityTypeConfiguration<InventoryAdjustment>
{
    public void Configure(EntityTypeBuilder<InventoryAdjustment> builder)
    {
        builder.Property(x => x.AdjustmentNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(x => x.AdjustmentNumber).IsUnique();

        builder.Property(x => x.ProductNameSnapshot).IsRequired().HasMaxLength(200);
        builder.Property(x => x.ProductCodeSnapshot).IsRequired().HasMaxLength(30);
        builder.Property(x => x.SkuSnapshot).HasMaxLength(60);
        builder.Property(x => x.Notes).HasMaxLength(500);

        // House convention for enums: stored as their member name, so a migration that reorders the
        // enum can never silently reinterpret historical rows.
        builder.Property(x => x.UnitSnapshot).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Direction).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Reason).HasConversion<string>().HasMaxLength(30);

        // 18,3 matches every other quantity column in the schema.
        builder.Property(x => x.AdjustmentQuantity).HasPrecision(18, 3);
        builder.Property(x => x.PreviousQuantity).HasPrecision(18, 3);
        builder.Property(x => x.NewQuantity).HasPrecision(18, 3);

        // The history page filters by date, product, reason and direction; these back the common
        // combinations without needing a scan as adjustments accumulate.
        builder.HasIndex(x => x.AdjustedAtUtc);
        builder.HasIndex(x => new { x.ProductId, x.AdjustedAtUtc });
        builder.HasIndex(x => x.Reason);

        builder.HasOne(x => x.Product).WithMany()
            .HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.AdjustedByUser).WithMany()
            .HasForeignKey(x => x.AdjustedByUserId).OnDelete(DeleteBehavior.Restrict);

        // Derived from Direction + AdjustmentQuantity; storing it would let the two drift apart.
        builder.Ignore(x => x.SignedQuantity);
    }
}
