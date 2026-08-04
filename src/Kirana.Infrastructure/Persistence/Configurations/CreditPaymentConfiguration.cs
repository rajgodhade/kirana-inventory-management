using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class CreditPaymentConfiguration : IEntityTypeConfiguration<CreditPayment>
{
    public void Configure(EntityTypeBuilder<CreditPayment> builder)
    {
        builder.Property(p => p.ReceiptNumber).IsRequired().HasMaxLength(30);
        builder.HasIndex(p => p.ReceiptNumber).IsUnique();

        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Amount).HasPrecision(18, 2);
        builder.Property(p => p.ReferenceNumber).HasMaxLength(100);
        builder.Property(p => p.Notes).HasMaxLength(500);
        builder.HasIndex(p => p.PaymentDateUtc);

        builder.HasOne(p => p.Customer)
            .WithMany()
            .HasForeignKey(p => p.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(p => p.RecordedByUser)
            .WithMany()
            .HasForeignKey(p => p.RecordedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.CustomerId);
    }
}

public sealed class CreditPaymentAllocationConfiguration : IEntityTypeConfiguration<CreditPaymentAllocation>
{
    public void Configure(EntityTypeBuilder<CreditPaymentAllocation> builder)
    {
        builder.Property(a => a.Amount).HasPrecision(18, 2);

        builder.HasOne(a => a.CreditPayment)
            .WithMany(p => p.Allocations)
            .HasForeignKey(a => a.CreditPaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(a => a.CustomerCredit)
            .WithMany()
            .HasForeignKey(a => a.CustomerCreditId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.CreditPaymentId);
        builder.HasIndex(a => a.CustomerCreditId);
    }
}
