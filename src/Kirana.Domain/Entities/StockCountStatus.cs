namespace Kirana.Domain.Entities;

/// <summary>
/// Lifecycle of a physical stock count (Phase 13C). Deliberately minimal: a count is either being
/// worked on, finished, or abandoned. There is no "approved" state distinct from
/// <see cref="Completed"/> — variance review is a UI step before finalization, not a persisted
/// status, so there is exactly one moment at which inventory changes.
/// </summary>
public enum StockCountStatus
{
    /// <summary>Being counted. The ONLY state in which items may be added, edited or removed, and
    /// the only state from which stock has not yet moved.</summary>
    InProgress,

    /// <summary>Finalized: variances have been applied to inventory as stock movements. Immutable —
    /// no item, quantity or note may change, and finalization can never run a second time.</summary>
    Completed,

    /// <summary>Abandoned without touching inventory. Kept rather than deleted so the audit trail
    /// still shows that a count was attempted.</summary>
    Cancelled,
}
