using Kirana.Domain.Entities;

namespace Kirana.Application.Inventories;

/// <summary>
/// A request to correct stock by hand (Phase 13D).
///
/// <para>Note what is deliberately absent: any "new quantity" field. The caller states the product,
/// the direction and a positive magnitude; the resulting quantity is computed by the service
/// against stock read fresh inside its own transaction. Accepting a client-supplied result would
/// let a stale screen overwrite whatever happened in the meantime.</para>
/// </summary>
public sealed class CreateInventoryAdjustmentRequest
{
    public int ProductId { get; init; }

    public InventoryAdjustmentDirection Direction { get; init; }

    /// <summary>Positive magnitude. The sign comes from <see cref="Direction"/>.</summary>
    public decimal Quantity { get; init; }

    public InventoryAdjustmentReason Reason { get; init; }

    public string? Notes { get; init; }

    public int? PerformedByUserId { get; init; }
}

/// <summary>
/// What an adjustment would do, computed without writing anything — backs the review step so the
/// operator confirms real numbers rather than a blind "Apply" button.
///
/// <para>Informational only. The service recomputes everything inside the transaction, so a preview
/// that has gone stale cannot influence the result.</para>
/// </summary>
public sealed record InventoryAdjustmentPreview(
    int ProductId,
    string ProductName,
    string ProductCode,
    UnitOfMeasure Unit,
    decimal CurrentQuantity,
    InventoryAdjustmentDirection Direction,
    decimal Quantity,
    InventoryAdjustmentReason Reason,
    string? Notes)
{
    public decimal SignedQuantity => Direction.ToSignedQuantity(Quantity);

    public decimal ResultingQuantity => CurrentQuantity + SignedQuantity;

    /// <summary>True when this decrease would drive stock below zero. Surfaced in the preview so the
    /// operator sees the problem before confirming — though the service refuses it regardless.</summary>
    public bool WouldGoNegative => ResultingQuantity < 0m;

    /// <summary>"120 → 115", the whole change in one glance.</summary>
    public string TransitionText => $"{CurrentQuantity:0.###} → {ResultingQuantity:0.###}";

    /// <summary>Always signed, so increase and decrease read differently without relying on colour.</summary>
    public string SignedQuantityText => $"{Direction.ToSignPrefix()}{Quantity:0.###}";
}

/// <summary>Filters for the adjustment history page.</summary>
public sealed class InventoryAdjustmentQuery
{
    /// <summary>Matches adjustment number, product name, product code, SKU, or notes.</summary>
    public string? SearchText { get; init; }

    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }

    public InventoryAdjustmentDirection? Direction { get; init; }
    public InventoryAdjustmentReason? Reason { get; init; }
    public int? ProductId { get; init; }
    public int? UserId { get; init; }

    public int MaxResults { get; init; } = 200;
}
