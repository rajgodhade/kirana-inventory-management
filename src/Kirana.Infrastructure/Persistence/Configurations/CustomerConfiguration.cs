using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.Property(c => c.Name).IsRequired().HasMaxLength(200);
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.HasIndex(c => c.Phone).IsUnique().HasFilter("\"Phone\" IS NOT NULL");
        builder.Property(c => c.Gstin).HasMaxLength(20);
        builder.Property(c => c.CreditBalance).HasPrecision(18, 2);
    }
}
