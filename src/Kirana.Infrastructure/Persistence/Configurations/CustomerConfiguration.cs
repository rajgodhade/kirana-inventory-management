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

        builder.Property(c => c.Gstin).HasMaxLength(15);
        builder.Property(c => c.StateCode).HasMaxLength(2);
        builder.Property(c => c.GstRegistrationType).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Address).HasMaxLength(500);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.CreditBalance).HasPrecision(18, 2);

        // Stored as the enum member name at 20 chars, the same convention every other enum in this
        // schema uses (PriceLevel on ProductPrice, MovementType, Symbology). Nullable, because "no
        // preference" is a real state and must not be written as a defaulted Retail row.
        builder.Property(c => c.DefaultPriceLevel).HasConversion<string>().HasMaxLength(20);
    }
}
