using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class SequenceCounterConfiguration : IEntityTypeConfiguration<SequenceCounter>
{
    public void Configure(EntityTypeBuilder<SequenceCounter> builder)
    {
        builder.Property(s => s.Key).IsRequired().HasMaxLength(50);
        builder.HasIndex(s => s.Key).IsUnique();
    }
}
