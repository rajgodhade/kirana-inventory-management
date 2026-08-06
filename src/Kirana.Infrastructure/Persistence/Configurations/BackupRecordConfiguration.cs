using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kirana.Infrastructure.Persistence.Configurations;

public sealed class BackupRecordConfiguration : IEntityTypeConfiguration<BackupRecord>
{
    public void Configure(EntityTypeBuilder<BackupRecord> builder)
    {
        builder.Property(b => b.FileName).IsRequired().HasMaxLength(260);
        builder.Property(b => b.FilePath).IsRequired().HasMaxLength(1000);
        builder.Property(b => b.ChecksumSha256).IsRequired().HasMaxLength(64);
        builder.Property(b => b.AppVersion).HasMaxLength(50);
        builder.Property(b => b.Notes).HasMaxLength(500);
        builder.Property(b => b.BackupType).HasConversion<string>().HasMaxLength(30);

        // SetNull rather than Cascade: deleting the user who took a backup must never delete the
        // backup history row itself — same reasoning as AuditLog.UserId.
        builder.HasOne(b => b.CreatedByUser)
            .WithMany()
            .HasForeignKey(b => b.CreatedByUserId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(b => b.CreatedAtUtc);
    }
}
