using Kirana.Domain.Entities;

namespace Kirana.Application.Inventories;

/// <summary>
/// Authorized manual corrections to stock (Phase 13D) — the path for quantity changes that are not
/// a sale, purchase, return, or physical stock count.
///
/// <para><b>Never silently overwrites stock.</b> There is no "set quantity to X" operation. Every
/// change states a direction, a magnitude and a reason, and produces three linked records in one
/// transaction: an <see cref="InventoryAdjustment"/> (why), a <see cref="StockMovement"/> (the
/// ledger entry), and an audit row. Inventory cannot move here without all three.</para>
///
/// <para>Gated by <see cref="PermissionKeys.InventoryManage"/> — the permission that already means
/// "may change stock levels" — enforced here, not merely in the UI.</para>
/// </summary>
public interface IInventoryAdjustmentService
{
    /// <summary>
    /// Computes what an adjustment would do, writing nothing. Reads current stock untracked so the
    /// review screen reflects live inventory rather than whatever the screen was opened with.
    /// </summary>
    Task<InventoryAdjustmentPreview> PreviewAsync(
        CreateInventoryAdjustmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the adjustment in ONE transaction: adjustment record, stock movement, quantity
    /// update and audit either all land or none do.
    ///
    /// <para>Stock is re-read fresh inside the transaction and the new quantity computed from that,
    /// never from anything the caller supplied — so a sale that happened while the operator was
    /// typing is respected instead of being overwritten.</para>
    ///
    /// <para>Refuses to drive stock negative, refuses a zero or negative magnitude, and refuses
    /// <see cref="InventoryAdjustmentReason.Other"/> without notes.</para>
    /// </summary>
    Task<InventoryAdjustment> CreateAsync(
        CreateInventoryAdjustmentRequest request, CancellationToken cancellationToken = default);

    /// <summary>History for the adjustments page, newest first.</summary>
    Task<IReadOnlyList<InventoryAdjustment>> SearchAsync(
        InventoryAdjustmentQuery query, CancellationToken cancellationToken = default);

    /// <summary>One adjustment with its product and user loaded, for the detail view.</summary>
    Task<InventoryAdjustment?> GetByIdAsync(int adjustmentId, CancellationToken cancellationToken = default);

    /// <summary>Current stock for the product-selection step, read untracked.</summary>
    Task<decimal> GetCurrentStockAsync(int productId, CancellationToken cancellationToken = default);
}
