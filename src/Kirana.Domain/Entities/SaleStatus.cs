namespace Kirana.Domain.Entities;

/// <summary>Only <see cref="Completed"/> is produced by Phase 4 — cancellation/refund status
/// values arrive with Phase 9 returns.</summary>
public enum SaleStatus
{
    Completed,
}
