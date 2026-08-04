using Kirana.Application.Abstractions;
using Kirana.Domain.Entities;

namespace Kirana.Infrastructure.Persistence;

public sealed class EfAuditLogger(KiranaDbContext db) : IAuditLogger
{
    public async Task RecordAsync(
        int? userId,
        string action,
        string entity,
        string? entityId = null,
        string? previousValue = null,
        string? newValue = null,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            Entity = entity,
            EntityId = entityId,
            PreviousValue = previousValue,
            NewValue = newValue,
            Reason = reason,
        });

        await db.SaveChangesAsync(cancellationToken);
    }
}
