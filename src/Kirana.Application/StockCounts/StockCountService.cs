using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.StockCounts;

public sealed class StockCountService(
    IKiranaDbContext db,
    ISequenceGenerator sequenceGenerator,
    IAuditLogger auditLogger,
    IPermissionEnforcer permissionEnforcer,
    IBarcodeLookupService barcodeLookup) : IStockCountService
{
    private const string SequenceKey = "StockCount";
    private const string SequencePrefix = "STK-COUNT";
    private const int SequencePadding = 6;

    /// <summary>Matches the 18,3 precision of every quantity column in the schema. Rounding on the
    /// way in means the stored value and the variance arithmetic agree exactly, rather than a
    /// 4th-decimal remainder surviving to produce a phantom non-zero variance.</summary>
    private const int QuantityScale = 3;

    // ---- Reads ----

    public Task<StockCount?> GetActiveAsync(CancellationToken cancellationToken = default) =>
        ItemsQuery().FirstOrDefaultAsync(c => c.Status == StockCountStatus.InProgress, cancellationToken);

    public Task<StockCount?> GetByIdAsync(int stockCountId, CancellationToken cancellationToken = default) =>
        ItemsQuery().FirstOrDefaultAsync(c => c.Id == stockCountId, cancellationToken);

    /// <summary>Items are loaded with their Product in one Include chain rather than lazily per row —
    /// a 500-product count would otherwise issue 500 follow-up queries to render the grid.</summary>
    private IQueryable<StockCount> ItemsQuery() =>
        db.StockCounts
            .Include(c => c.StartedByUser)
            .Include(c => c.CompletedByUser)
            .Include(c => c.Items.OrderBy(i => i.Id))
                .ThenInclude(i => i.Product);

    public async Task<IReadOnlyList<StockCountSummary>> GetSummariesAsync(
        int take = 100, CancellationToken cancellationToken = default) =>
        await db.StockCounts
            .OrderByDescending(c => c.StartedAtUtc)
            .Take(take)
            // Projected server-side: the list page needs three counts per row, not the items.
            .Select(c => new StockCountSummary(
                c.Id,
                c.CountNumber,
                c.Status,
                c.StartedAtUtc,
                c.CompletedAtUtc,
                c.StartedByUser != null ? c.StartedByUser.FullName : null,
                c.Items.Count,
                c.Items.Count(i => i.CountedQuantity != null),
                c.Items.Count(i => i.CountedQuantity != null && i.CountedQuantity != i.SystemQuantity)))
            .ToListAsync(cancellationToken);

    // ---- Lifecycle ----

    public async Task<StockCount> StartAsync(
        string? notes, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        if (await db.StockCounts.AnyAsync(c => c.Status == StockCountStatus.InProgress, cancellationToken))
        {
            throw new InvalidOperationException(
                "A stock count is already in progress. Complete or cancel it before starting another.");
        }

        var stockCount = new StockCount
        {
            CountNumber = await sequenceGenerator.NextAsync(SequenceKey, SequencePrefix, SequencePadding, cancellationToken),
            Status = StockCountStatus.InProgress,
            StartedAtUtc = DateTime.UtcNow,
            StartedByUserId = performedByUserId,
            Notes = Trim(notes),
        };

        db.StockCounts.Add(stockCount);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "StockCountStarted", nameof(StockCount), stockCount.Id.ToString(),
            newValue: stockCount.CountNumber, cancellationToken: cancellationToken);

        return stockCount;
    }

    public async Task<StockCountItem> AddItemAsync(
        int stockCountId, int productId, string? barcodeSnapshot, int? performedByUserId,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        var stockCount = await LoadEditableAsync(stockCountId, cancellationToken);

        // One item per product per count. Returning the existing row rather than throwing makes a
        // double scan a no-op instead of an error the counter has to dismiss mid-aisle.
        var existing = await db.StockCountItems
            .FirstOrDefaultAsync(i => i.StockCountId == stockCountId && i.ProductId == productId, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var product = await db.Products
            .Include(p => p.Inventory)
            .FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        if (!product.IsActive)
        {
            throw new InvalidOperationException($"'{product.Name}' is inactive and cannot be counted.");
        }

        var item = new StockCountItem
        {
            StockCountId = stockCount.Id,
            ProductId = product.Id,
            ProductNameSnapshot = product.Name,
            ProductCodeSnapshot = product.ProductCode,
            SkuSnapshot = product.Sku,
            BarcodeSnapshot = barcodeSnapshot,
            UnitSnapshot = product.Unit,
            // Frozen here on purpose: the variance the counter sees must not shift under them
            // because a sale happened while they were walking the aisle.
            SystemQuantity = product.Inventory?.QuantityOnHand ?? 0m,
        };

        db.StockCountItems.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        return item;
    }

    public async Task<StockCountItem> AddItemByBarcodeAsync(
        int stockCountId, string barcode, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        if (string.IsNullOrWhiteSpace(barcode))
        {
            throw new ArgumentException("Barcode is required.", nameof(barcode));
        }

        // Reuses the Phase 13B lookup wholesale rather than re-querying ProductBarcodes here, so
        // every code of a product resolves to the same product and retired codes / inactive
        // products are refused by exactly the same rule the POS uses.
        var product = await barcodeLookup.LookupAsync(barcode, cancellationToken)
            ?? throw new InvalidOperationException($"No active product found for barcode '{barcode.Trim()}'.");

        return await AddItemAsync(stockCountId, product.Id, barcode.Trim(), performedByUserId, cancellationToken);
    }

    public async Task<StockCountItem> SetCountedQuantityAsync(
        int stockCountItemId, decimal countedQuantity, string? notes, int? performedByUserId,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        var item = await db.StockCountItems
            .Include(i => i.StockCount)
            .FirstOrDefaultAsync(i => i.Id == stockCountItemId, cancellationToken)
            ?? throw new InvalidOperationException("Stock count item not found.");

        EnsureEditable(item.StockCount);

        if (countedQuantity < 0m)
        {
            throw new ArgumentException(
                "Physical quantity cannot be negative.", nameof(countedQuantity));
        }

        if (!item.UnitSnapshot.SupportsDecimalQuantity() && countedQuantity != decimal.Truncate(countedQuantity))
        {
            throw new ArgumentException(
                $"{item.ProductNameSnapshot} is counted in {item.UnitSnapshot.ToDisplayText()}, which cannot hold a fractional quantity.",
                nameof(countedQuantity));
        }

        item.CountedQuantity = Math.Round(countedQuantity, QuantityScale, MidpointRounding.AwayFromZero);
        item.CountedAtUtc = DateTime.UtcNow;
        item.Notes = Trim(notes) ?? item.Notes;
        item.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "StockCountProductCounted", nameof(StockCount), item.StockCountId.ToString(),
            previousValue: item.SystemQuantity.ToString("0.###"),
            newValue: $"{item.ProductCodeSnapshot} counted {item.CountedQuantity.Value.ToString("0.###")}",
            cancellationToken: cancellationToken);

        return item;
    }

    public async Task RemoveItemAsync(
        int stockCountItemId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        var item = await db.StockCountItems
            .Include(i => i.StockCount)
            .FirstOrDefaultAsync(i => i.Id == stockCountItemId, cancellationToken)
            ?? throw new InvalidOperationException("Stock count item not found.");

        EnsureEditable(item.StockCount);

        db.StockCountItems.Remove(item);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task SetNotesAsync(
        int stockCountId, string? notes, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        var stockCount = await LoadEditableAsync(stockCountId, cancellationToken);
        stockCount.Notes = Trim(notes);
        stockCount.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelAsync(
        int stockCountId, string? reason, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        var stockCount = await LoadEditableAsync(stockCountId, cancellationToken);

        stockCount.Status = StockCountStatus.Cancelled;
        stockCount.CompletedAtUtc = DateTime.UtcNow;
        stockCount.CompletedByUserId = performedByUserId;
        stockCount.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "StockCountCancelled", nameof(StockCount), stockCount.Id.ToString(),
            newValue: stockCount.CountNumber, reason: Trim(reason), cancellationToken: cancellationToken);
    }

    // ---- Variance review ----

    public async Task<StockCountVariancePreview> GetVariancePreviewAsync(
        int stockCountId, CancellationToken cancellationToken = default)
    {
        var stockCount = await db.StockCounts
            .Include(c => c.Items.OrderBy(i => i.Id))
            .FirstOrDefaultAsync(c => c.Id == stockCountId, cancellationToken)
            ?? throw new InvalidOperationException("Stock count not found.");

        // Live stock for exactly the counted products, fetched as one keyed lookup rather than a
        // per-item query — this runs on every open of the review screen.
        var currentStock = await CurrentStockAsync(stockCount, cancellationToken);

        var lines = stockCount.Items
            .Select(i => new StockCountVarianceLine(
                i.Id, i.ProductId, i.ProductNameSnapshot, i.ProductCodeSnapshot, i.UnitSnapshot,
                i.SystemQuantity, i.CountedQuantity,
                currentStock.GetValueOrDefault(i.ProductId)))
            .ToList();

        var counted = lines.Where(l => l.CountedQuantity is not null).ToList();
        var increases = counted.Where(l => l.AppliedAdjustment > 0m).ToList();
        var decreases = counted.Where(l => l.AppliedAdjustment < 0m).ToList();

        return new StockCountVariancePreview(
            stockCount.Id,
            stockCount.CountNumber,
            stockCount.Items.Count,
            counted.Count,
            stockCount.Items.Count - counted.Count,
            increases.Count,
            decreases.Count,
            counted.Count - increases.Count - decreases.Count,
            increases.Sum(l => l.AppliedAdjustment),
            Math.Abs(decreases.Sum(l => l.AppliedAdjustment)),
            lines);
    }

    private async Task<Dictionary<int, decimal>> CurrentStockAsync(
        StockCount stockCount, CancellationToken cancellationToken)
    {
        var productIds = stockCount.Items.Select(i => i.ProductId).ToList();

        // AsNoTracking is load-bearing, not an optimisation. Without it EF returns the quantity from
        // any Inventory entity this context already tracks, and a count screen that has been open
        // for an hour is tracking stale values — so the "current stock" the rebase compares against
        // would be the figure from when the item was added, silently defeating the whole check.
        return await db.Inventories
            .AsNoTracking()
            .Where(inv => productIds.Contains(inv.ProductId))
            .ToDictionaryAsync(inv => inv.ProductId, inv => inv.QuantityOnHand, cancellationToken);
    }

    // ---- Finalization ----

    public async Task<StockCountResult> FinalizeAsync(
        int stockCountId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        // An explicit transaction, not this codebase's usual single-SaveChangesAsync: finalization
        // also writes an audit row through a separate service call, and a crash between the stock
        // write and the audit write would leave inventory moved with no record of why. Modelled on
        // CashRegisterService, the existing precedent for a multi-step atomic operation.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var stockCount = await db.StockCounts
                .Include(c => c.Items.OrderBy(i => i.Id))
                .FirstOrDefaultAsync(c => c.Id == stockCountId, cancellationToken)
                ?? throw new InvalidOperationException("Stock count not found.");

            // Re-checked inside the transaction, so two concurrent finalize calls cannot both pass:
            // the second finds the status already Completed and is rejected rather than
            // double-applying every variance.
            EnsureEditable(stockCount);

            var counted = stockCount.Items.Where(i => i.CountedQuantity is not null).ToList();
            if (counted.Count == 0)
            {
                throw new InvalidOperationException(
                    "Nothing has been counted yet. Record at least one physical quantity before finalizing.");
            }

            var countedProductIds = counted.Select(c => c.ProductId).ToList();
            var inventories = await db.Inventories
                .Where(inv => countedProductIds.Contains(inv.ProductId))
                .ToDictionaryAsync(inv => inv.ProductId, cancellationToken);

            // The entities above must be TRACKED (they are about to be written), but a long-lived
            // context may already hold them with quantities read when the count started — EF's
            // identity map returns the cached instance and the requery silently does nothing.
            // So the authoritative quantities are re-read untracked, inside this transaction, and
            // copied onto the tracked entities.
            //
            // Without this the rebase compares against a stale figure, which defeats the entire
            // concurrency protection: a sale made from another context during the count would be
            // invisible here and its units silently overwritten. Found by live E2E, not by the unit
            // tests, because those mutate stock through the same context and so never go stale.
            var liveQuantities = await db.Inventories
                .AsNoTracking()
                .Where(inv => countedProductIds.Contains(inv.ProductId))
                .ToDictionaryAsync(inv => inv.ProductId, inv => inv.QuantityOnHand, cancellationToken);

            foreach (var (productId, inventory) in inventories)
            {
                if (liveQuantities.TryGetValue(productId, out var liveQuantity))
                {
                    inventory.QuantityOnHand = liveQuantity;
                }
            }

            var nowUtc = DateTime.UtcNow;
            var increased = 0;
            var decreased = 0;
            var rebased = 0;
            var totalIncrease = 0m;
            var totalDecrease = 0m;

            foreach (var item in counted)
            {
                if (!inventories.TryGetValue(item.ProductId, out var inventory))
                {
                    // A counted product with no inventory row (never stocked). Create it so the
                    // count still lands, rather than silently dropping the line.
                    inventory = new Inventory { ProductId = item.ProductId, QuantityOnHand = 0m };
                    db.Inventories.Add(inventory);
                    inventories[item.ProductId] = inventory;
                }

                var currentQuantity = inventory.QuantityOnHand;

                // THE CONCURRENCY RULE. The adjustment is computed against CURRENT stock, not the
                // count-time snapshot, so the result always lands on the counted figure. Applying
                // the observed variance blindly would overshoot whenever stock moved mid-count:
                // snapshot 100, now 98, counted 97 -> a stale -3 lands on 95, but -1 lands on 97.
                // The snapshot is preserved untouched for reporting; only the applied delta rebases.
                var adjustment = item.CountedQuantity!.Value - currentQuantity;

                if (currentQuantity != item.SystemQuantity)
                {
                    // Recorded so the completion summary and any later audit can explain why this
                    // line's applied adjustment differs from the variance the counter wrote down.
                    item.SystemQuantityAtFinalization = currentQuantity;
                    rebased++;
                }

                if (adjustment == 0m)
                {
                    // Zero variance writes no movement at all. A ledger row saying "nothing
                    // changed" is noise that makes real shrinkage harder to spot.
                    continue;
                }

                inventory.QuantityOnHand = currentQuantity + adjustment;
                inventory.UpdatedAtUtc = nowUtc;

                db.StockMovements.Add(new StockMovement
                {
                    ProductId = item.ProductId,
                    MovementType = adjustment > 0m
                        ? StockMovementType.StockCountIncrease
                        : StockMovementType.StockCountDecrease,
                    QuantityChange = adjustment,
                    PreviousQuantity = currentQuantity,
                    NewQuantity = inventory.QuantityOnHand,
                    TimestampUtc = nowUtc,
                    UserId = performedByUserId,
                    ReferenceType = nameof(StockCount),
                    ReferenceId = stockCount.CountNumber,
                    Reason = "Physical stock count",
                });

                if (adjustment > 0m)
                {
                    increased++;
                    totalIncrease += adjustment;
                }
                else
                {
                    decreased++;
                    totalDecrease += Math.Abs(adjustment);
                }
            }

            stockCount.Status = StockCountStatus.Completed;
            stockCount.CompletedAtUtc = nowUtc;
            stockCount.CompletedByUserId = performedByUserId;
            stockCount.RebasedItemCount = rebased;
            stockCount.UpdatedAtUtc = nowUtc;

            await db.SaveChangesAsync(cancellationToken);

            await auditLogger.RecordAsync(
                performedByUserId, "StockCountCompleted", nameof(StockCount), stockCount.Id.ToString(),
                newValue:
                    $"{stockCount.CountNumber}: {counted.Count} counted, {increased} increased (+{totalIncrease:0.###}), " +
                    $"{decreased} decreased (-{totalDecrease:0.###}), {increased + decreased} adjustments" +
                    (rebased > 0 ? $", {rebased} rebased onto live stock" : string.Empty),
                cancellationToken: cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new StockCountResult(
                stockCount.Id,
                stockCount.CountNumber,
                counted.Count,
                increased,
                decreased,
                counted.Count - increased - decreased,
                increased + decreased,
                rebased,
                totalIncrease,
                totalDecrease);
        }
        catch
        {
            // Any failure — validation, a DB error, or the audit write — unwinds every stock
            // movement and quantity change. There is deliberately no partial-finalization path.
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ---- Helpers ----

    private async Task<StockCount> LoadEditableAsync(int stockCountId, CancellationToken cancellationToken)
    {
        var stockCount = await db.StockCounts.FirstOrDefaultAsync(c => c.Id == stockCountId, cancellationToken)
            ?? throw new InvalidOperationException("Stock count not found.");

        EnsureEditable(stockCount);
        return stockCount;
    }

    /// <summary>A completed or cancelled count is immutable. Enforced in one place so no mutation
    /// path can forget it — including finalization itself, which is what stops a count being
    /// finalized twice and applying every variance again.</summary>
    private static void EnsureEditable(StockCount stockCount)
    {
        if (stockCount.Status != StockCountStatus.InProgress)
        {
            throw new InvalidOperationException(
                $"Stock count {stockCount.CountNumber} is {stockCount.Status.ToString().ToLowerInvariant()} and can no longer be changed.");
        }
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
