using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.Property(m => m.QuantityChange).HasPrecision(18, 3);
        builder.Property(m => m.PreviousQuantity).HasPrecision(18, 3);
        builder.Property(m => m.NewQuantity).HasPrecision(18, 3);
        builder.Property(m => m.MovementType).HasConversion<string>().HasMaxLength(30);

        builder.Property(m => m.ReferenceType).HasMaxLength(50);
        builder.Property(m => m.ReferenceId).HasMaxLength(50);
        builder.Property(m => m.Reason).HasMaxLength(500);

        builder.HasOne(m => m.Product)
            .WithMany(p => p.StockMovements)
            .HasForeignKey(m => m.ProductId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(m => m.User)
            .WithMany()
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(m => m.ProductId);
        builder.HasIndex(m => m.TimestampUtc);
    }
}
