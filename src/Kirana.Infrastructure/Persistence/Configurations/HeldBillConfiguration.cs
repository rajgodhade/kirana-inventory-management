using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class HeldBillConfiguration : IEntityTypeConfiguration<HeldBill>
{
    public void Configure(EntityTypeBuilder<HeldBill> builder)
    {
        builder.Property(h => h.BillDiscountPercent).HasPrecision(5, 2);
        builder.Property(h => h.Note).HasMaxLength(200);

        builder.HasOne(h => h.CashierUser)
            .WithMany()
            .HasForeignKey(h => h.CashierUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(h => h.Customer)
            .WithMany()
            .HasForeignKey(h => h.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => h.HeldAtUtc);
    }
}

public sealed class HeldBillItemConfiguration : IEntityTypeConfiguration<HeldBillItem>
{
    public void Configure(EntityTypeBuilder<HeldBillItem> builder)
    {
        builder.Property(i => i.Quantity).HasPrecision(18, 3);
        builder.Property(i => i.DiscountPercent).HasPrecision(5, 2);

        builder.HasOne(i => i.HeldBill)
            .WithMany(h => h.Items)
            .HasForeignKey(i => i.HeldBillId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(i => i.Product)
            .WithMany()
            .HasForeignKey(i => i.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
