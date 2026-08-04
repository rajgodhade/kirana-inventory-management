using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        // Indexed for fast POS lookup by Customer ID / phone / name (PRD §30, §45).
        builder.Property(c => c.CustomerCode).IsRequired().HasMaxLength(30);
        builder.HasIndex(c => c.CustomerCode).IsUnique();

        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(c => c.Name);

        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.HasIndex(c => c.Phone).IsUnique().HasFilter("\"Phone\" IS NOT NULL");

        builder.Property(c => c.Gstin).HasMaxLength(20);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.CreditBalance).HasPrecision(18, 2);
    }
}
