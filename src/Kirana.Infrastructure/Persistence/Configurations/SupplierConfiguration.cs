using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.Property(s => s.SupplierCode).IsRequired().HasMaxLength(30);
        builder.HasIndex(s => s.SupplierCode).IsUnique();

        builder.Property(s => s.Name).IsRequired().HasMaxLength(200);
        builder.HasIndex(s => s.Name);
        builder.Property(s => s.Gstin).HasMaxLength(15);
        builder.Property(s => s.StateCode).HasMaxLength(2);
        builder.Property(s => s.GstRegistrationType).HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.ContactPerson).HasMaxLength(200);
        builder.Property(s => s.Phone).HasMaxLength(20);
        builder.Property(s => s.Email).HasMaxLength(200);
        builder.Property(s => s.Address).HasMaxLength(500);
        builder.Property(s => s.OutstandingBalance).HasPrecision(18, 2);
    }
}
