using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Audit;

public sealed class AuditLogQueryService(IKiranaDbContext db, IPermissionEnforcer permissionEnforcer) : IAuditLogQueryService
{
    public async Task<IReadOnlyList<AuditLog>> SearchAsync(AuditLogQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.AuditLogView, cancellationToken);

        var filtered = db.AuditLogs.Include(a => a.User).AsQueryable();

        if (query.UserId is { } userId)
        {
            filtered = filtered.Where(a => a.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            filtered = filtered.Where(a => a.Action == query.Action);
        }

        if (!string.IsNullOrWhiteSpace(query.Entity))
        {
            filtered = filtered.Where(a => a.Entity == query.Entity);
        }

        if (query.FromUtc is { } fromUtc)
        {
            filtered = filtered.Where(a => a.TimestampUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            filtered = filtered.Where(a => a.TimestampUtc <= toUtc);
        }

        return await filtered
            .OrderByDescending(a => a.TimestampUtc)
            .Take(query.MaxResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetDistinctActionsAsync(CancellationToken cancellationToken = default) =>
        await db.AuditLogs.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(cancellationToken);
}
