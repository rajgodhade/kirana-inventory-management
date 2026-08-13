using Kirana.Domain.Entities;

namespace Kirana.Application.StockCounts;

/// <summary>
/// Physical stock counting (Phase 13C). Lets a shopkeeper record what is actually on the shelves and
/// reconcile it against system stock.
///
/// <para><b>The central rule: counting never moves stock.</b> Every method here except
/// <see cref="FinalizeAsync"/> leaves <see cref="Inventory.QuantityOnHand"/> and
/// <see cref="StockMovement"/> completely untouched. Inventory changes at exactly one moment, in one
/// transaction, when the operator explicitly finalizes.</para>
///
/// <para>All mutations require <see cref="PermissionKeys.InventoryManage"/> — the permission that
/// already governs stock adjustments — enforced here at the service layer, not merely in the UI.</para>
/// </summary>
public interface IStockCountService
{
    /// <summary>The count currently being worked on, with its items, or null when none is open.</summary>
    Task<StockCount?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>One count with its items loaded, for the active-count and detail screens.</summary>
    Task<StockCount?> GetByIdAsync(int stockCountId, CancellationToken cancellationToken = default);

    /// <summary>History for the list page, newest first.</summary>
    Task<IReadOnlyList<StockCountSummary>> GetSummariesAsync(int take = 100, CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a new count. Throws when one is already in progress — only one store-wide count may be
    /// open at a time. Creates no items and moves no stock; products are added as they are counted.
    /// </summary>
    Task<StockCount> StartAsync(string? notes, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a product to the open count, snapshotting its identity, unit and CURRENT stock. Returns
    /// the existing item when the product is already in the count rather than creating a second one,
    /// so a double scan is harmless.
    /// </summary>
    Task<StockCountItem> AddItemAsync(
        int stockCountId, int productId, string? barcodeSnapshot, int? performedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a scanned barcode through the shared Phase 13B lookup and adds/returns that
    /// product's item. Any active barcode of a product resolves to the same single item. Throws when
    /// the code is unknown, retired, or belongs to an inactive product.
    /// </summary>
    Task<StockCountItem> AddItemByBarcodeAsync(
        int stockCountId, string barcode, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the physically counted quantity. Rejects negatives, and rejects fractional values for
    /// units that cannot hold them (a shelf has 3 packets, never 3.5). Still moves no stock.
    /// </summary>
    Task<StockCountItem> SetCountedQuantityAsync(
        int stockCountItemId, decimal countedQuantity, string? notes, int? performedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Removes an item from an in-progress count — for something added by mistake.</summary>
    Task RemoveItemAsync(int stockCountItemId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Updates the count's free-text notes while it is in progress.</summary>
    Task SetNotesAsync(int stockCountId, string? notes, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Computes what finalization would do, writing nothing. Reads live inventory so the review
    /// screen can show which lines will be rebased because stock moved during the count.
    /// </summary>
    Task<StockCountVariancePreview> GetVariancePreviewAsync(
        int stockCountId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies the count to inventory in ONE transaction: stock movements, quantity updates, and the
    /// status change to Completed either all land or none do. Uncounted items are skipped entirely
    /// (never treated as zero). Adjustments are rebased onto live stock, so the result is always the
    /// counted figure even if sales happened mid-count.
    /// </summary>
    Task<StockCountResult> FinalizeAsync(
        int stockCountId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Abandons an in-progress count without touching inventory.</summary>
    Task CancelAsync(int stockCountId, string? reason, int? performedByUserId, CancellationToken cancellationToken = default);
}
