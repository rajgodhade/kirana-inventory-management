using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class CustomerCreditConfiguration : IEntityTypeConfiguration<CustomerCredit>
{
    public void Configure(EntityTypeBuilder<CustomerCredit> builder)
    {
        builder.Property(c => c.Amount).HasPrecision(18, 2);
        builder.Property(c => c.RemainingAmount).HasPrecision(18, 2);
        builder.Property(c => c.Notes).HasMaxLength(500);

        builder.HasOne(c => c.Customer)
            .WithMany()
            .HasForeignKey(c => c.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(c => c.Sale)
            .WithMany()
            .HasForeignKey(c => c.SaleId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(c => c.CustomerId);
    }
}
