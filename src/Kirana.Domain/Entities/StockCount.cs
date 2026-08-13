using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// A physical stock-take (Phase 13C): the shopkeeper walks the shelves, records what is actually
/// there, and the difference against system stock is applied as inventory adjustments — but only
/// when the count is explicitly finalized.
///
/// <para><b>Counting never moves stock.</b> While <see cref="Status"/> is
/// <see cref="StockCountStatus.InProgress"/> this aggregate is pure record-keeping: no
/// <see cref="Inventory"/> row and no <see cref="StockMovement"/> is touched. That is what lets a
/// count run for an hour across a busy shop floor without corrupting live inventory, and it is why
/// each item carries its own system-quantity snapshot.</para>
///
/// <para>Scope is the whole store. Only one count may be InProgress at a time (enforced by a
/// filtered unique index), because two overlapping counts would each snapshot the same products and
/// then apply conflicting adjustments.</para>
/// </summary>
public class StockCount : Entity
{
    /// <summary>Human-readable identifier, e.g. "STK-COUNT-000001". Issued from the shared
    /// sequence infrastructure, so it is unique and gap-free under concurrent callers.</summary>
    public string CountNumber { get; set; } = string.Empty;

    public StockCountStatus Status { get; set; } = StockCountStatus.InProgress;

    public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

    public int? StartedByUserId { get; set; }
    public User? StartedByUser { get; set; }

    /// <summary>Set when the count reaches <see cref="StockCountStatus.Completed"/> or
    /// <see cref="StockCountStatus.Cancelled"/>; null while in progress.</summary>
    public DateTime? CompletedAtUtc { get; set; }

    public int? CompletedByUserId { get; set; }
    public User? CompletedByUser { get; set; }

    public string? Notes { get; set; }

    /// <summary>How many items had their stock rebased at finalization because live inventory moved
    /// after the count started (see <see cref="StockCountItem.SystemQuantityAtFinalization"/>).
    /// Stored so the completion summary can be reconstructed later without recomputing it.</summary>
    public int RebasedItemCount { get; set; }

    public ICollection<StockCountItem> Items { get; set; } = new List<StockCountItem>();
}
