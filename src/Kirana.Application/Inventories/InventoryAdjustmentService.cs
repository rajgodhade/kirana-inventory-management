using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Inventories;

public sealed class InventoryAdjustmentService(
    IKiranaDbContext db,
    ISequenceGenerator sequenceGenerator,
    IAuditLogger auditLogger,
    IPermissionEnforcer permissionEnforcer) : IInventoryAdjustmentService
{
    private const string SequenceKey = "InventoryAdjustment";
    private const string SequencePrefix = "ADJ";
    private const int SequencePadding = 6;

    /// <summary>Matches the 18,3 precision of every quantity column, so the stored magnitude and
    /// the arithmetic agree exactly rather than leaving a 4th-decimal remainder.</summary>
    private const int QuantityScale = 3;

    // ---- Reads ----

    /// <summary>
    /// Current stock, read UNTRACKED. This is deliberate and load-bearing: EF's identity map returns
    /// an already-tracked entity if the context has one, and a screen that has been open for a while
    /// is tracking a stale quantity. Phase 13C shipped that exact bug — the requery looked correct
    /// and silently returned the old value.
    /// </summary>
    public async Task<decimal> GetCurrentStockAsync(int productId, CancellationToken cancellationToken = default) =>
        await db.Inventories
            .AsNoTracking()
            .Where(i => i.ProductId == productId)
            .Select(i => (decimal?)i.QuantityOnHand)
            .FirstOrDefaultAsync(cancellationToken) ?? 0m;

    public async Task<InventoryAdjustmentPreview> PreviewAsync(
        CreateInventoryAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        var product = await db.Products
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        var current = await GetCurrentStockAsync(request.ProductId, cancellationToken);

        return new InventoryAdjustmentPreview(
            product.Id,
            product.Name,
            product.ProductCode,
            product.Unit,
            current,
            request.Direction,
            Round(request.Quantity),
            request.Reason,
            Trim(request.Notes));
    }

    public async Task<IReadOnlyList<InventoryAdjustment>> SearchAsync(
        InventoryAdjustmentQuery query, CancellationToken cancellationToken = default)
    {
        // AsNoTracking throughout: this is a read-only history list, and tracking hundreds of rows
        // would both cost memory and risk handing stale entities to a later write.
        IQueryable<InventoryAdjustment> adjustments = db.InventoryAdjustments
            .AsNoTracking()
            .Include(a => a.AdjustedByUser);

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var like = $"%{query.SearchText.Trim()}%";
            adjustments = adjustments.Where(a =>
                EF.Functions.Like(a.AdjustmentNumber, like) ||
                EF.Functions.Like(a.ProductNameSnapshot, like) ||
                EF.Functions.Like(a.ProductCodeSnapshot, like) ||
                (a.SkuSnapshot != null && EF.Functions.Like(a.SkuSnapshot, like)) ||
                (a.Notes != null && EF.Functions.Like(a.Notes, like)));
        }

        if (query.FromUtc is { } fromUtc)
        {
            adjustments = adjustments.Where(a => a.AdjustedAtUtc >= fromUtc);
        }

        if (query.ToUtc is { } toUtc)
        {
            adjustments = adjustments.Where(a => a.AdjustedAtUtc < toUtc);
        }

        if (query.Direction is { } direction)
        {
            adjustments = adjustments.Where(a => a.Direction == direction);
        }

        if (query.Reason is { } reason)
        {
            adjustments = adjustments.Where(a => a.Reason == reason);
        }

        if (query.ProductId is { } productId)
        {
            adjustments = adjustments.Where(a => a.ProductId == productId);
        }

        if (query.UserId is { } userId)
        {
            adjustments = adjustments.Where(a => a.AdjustedByUserId == userId);
        }

        return await adjustments
            .OrderByDescending(a => a.AdjustedAtUtc)
            .ThenByDescending(a => a.Id)
            .Take(query.MaxResults)
            .ToListAsync(cancellationToken);
    }

    public Task<InventoryAdjustment?> GetByIdAsync(int adjustmentId, CancellationToken cancellationToken = default) =>
        db.InventoryAdjustments
            .AsNoTracking()
            .Include(a => a.Product)
            .Include(a => a.AdjustedByUser)
            .FirstOrDefaultAsync(a => a.Id == adjustmentId, cancellationToken);

    // ---- The write ----

    public async Task<InventoryAdjustment> CreateAsync(
        CreateInventoryAdjustmentRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(
            request.PerformedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        // Input validation happens before the transaction opens: these checks need no database
        // state, and failing early keeps a bad request from ever holding a write lock.
        var quantity = ValidateQuantity(request.Quantity);
        var notes = ValidateNotes(request.Reason, request.Notes);

        // One transaction covering the adjustment record, the movement, the quantity update and the
        // audit row. Any of those landing without the others would be unrecoverable without manual
        // database surgery — inventory changed with no explanation, or an explanation with no
        // change. Same shape as StockCountService.FinalizeAsync and CashRegisterService.
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        try
        {
            var product = await db.Products
                .FirstOrDefaultAsync(p => p.Id == request.ProductId, cancellationToken)
                ?? throw new InvalidOperationException("Product not found.");

            var inventory = await db.Inventories
                .FirstOrDefaultAsync(i => i.ProductId == request.ProductId, cancellationToken);

            if (inventory is null)
            {
                inventory = new Inventory { ProductId = product.Id, QuantityOnHand = 0m };
                db.Inventories.Add(inventory);
            }
            else
            {
                // The tracked entity above may carry a quantity this context read minutes ago. The
                // authoritative value is re-read untracked, inside the transaction, and copied onto
                // it — so an adjustment computes against stock as it is NOW, not as the screen
                // remembers it. This is the Phase 13C identity-map lesson applied up front.
                var liveQuantity = await db.Inventories
                    .AsNoTracking()
                    .Where(i => i.ProductId == request.ProductId)
                    .Select(i => (decimal?)i.QuantityOnHand)
                    .FirstOrDefaultAsync(cancellationToken);

                if (liveQuantity is { } live)
                {
                    inventory.QuantityOnHand = live;
                }
            }

            var previousQuantity = inventory.QuantityOnHand;
            var signedChange = request.Direction.ToSignedQuantity(quantity);
            var newQuantity = previousQuantity + signedChange;

            // Enforced in the service, never left to the UI. Stock going negative is not a display
            // problem — it corrupts valuation and every downstream report.
            if (newQuantity < 0m)
            {
                throw new InvalidOperationException(
                    $"Cannot decrease {product.Name} by {quantity:0.###}: only {previousQuantity:0.###} " +
                    $"{product.Unit.ToDisplayText()} in stock. Stock cannot go negative.");
            }

            var nowUtc = DateTime.UtcNow;

            var adjustment = new InventoryAdjustment
            {
                AdjustmentNumber = await sequenceGenerator.NextAsync(
                    SequenceKey, SequencePrefix, SequencePadding, cancellationToken),
                ProductId = product.Id,
                ProductNameSnapshot = product.Name,
                ProductCodeSnapshot = product.ProductCode,
                SkuSnapshot = product.Sku,
                UnitSnapshot = product.Unit,
                Direction = request.Direction,
                AdjustmentQuantity = quantity,
                PreviousQuantity = previousQuantity,
                NewQuantity = newQuantity,
                Reason = request.Reason,
                Notes = notes,
                AdjustedAtUtc = nowUtc,
                AdjustedByUserId = request.PerformedByUserId,
            };
            db.InventoryAdjustments.Add(adjustment);

            inventory.QuantityOnHand = newQuantity;
            inventory.UpdatedAtUtc = nowUtc;

            db.StockMovements.Add(new StockMovement
            {
                ProductId = product.Id,
                MovementType = request.Direction == InventoryAdjustmentDirection.Increase
                    ? StockMovementType.InventoryAdjustmentIncrease
                    : StockMovementType.InventoryAdjustmentDecrease,
                QuantityChange = signedChange,
                PreviousQuantity = previousQuantity,
                NewQuantity = newQuantity,
                TimestampUtc = nowUtc,
                UserId = request.PerformedByUserId,
                ReferenceType = nameof(InventoryAdjustment),
                ReferenceId = adjustment.AdjustmentNumber,
                Reason = request.Reason.ToDisplayText(),
            });

            await db.SaveChangesAsync(cancellationToken);

            // Enough detail to reconstruct the change without joining anything: what moved, by how
            // much, from what to what, why, and on whose authority.
            await auditLogger.RecordAsync(
                request.PerformedByUserId,
                "InventoryAdjusted",
                nameof(InventoryAdjustment),
                adjustment.Id.ToString(),
                previousValue: previousQuantity.ToString("0.###"),
                newValue:
                    $"{adjustment.AdjustmentNumber}: {product.ProductCode} {product.Name} " +
                    $"{request.Direction.ToSignPrefix()}{quantity:0.###} {product.Unit.ToDisplayText()} " +
                    $"({previousQuantity:0.###} -> {newQuantity:0.###}), reason {request.Reason.ToDisplayText()}",
                reason: notes ?? request.Reason.ToDisplayText(),
                cancellationToken: cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return adjustment;
        }
        catch
        {
            // Includes the negative-stock refusal: nothing is written, so a rejected adjustment
            // leaves no record, no movement and no partial audit.
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    // ---- Validation ----

    private static decimal ValidateQuantity(decimal quantity)
    {
        // Direction carries the sign, so a negative magnitude here means the caller is trying to
        // express direction twice — reject rather than guess which one wins.
        if (quantity <= 0m)
        {
            throw new ArgumentException(
                "Adjustment quantity must be greater than zero. Use the direction to increase or decrease.",
                nameof(quantity));
        }

        var rounded = Math.Round(quantity, QuantityScale, MidpointRounding.AwayFromZero);

        // A magnitude that rounds away to nothing would produce a zero-effect adjustment: a record
        // and a ledger row that change no stock, which is noise in an audit trail.
        if (rounded <= 0m)
        {
            throw new ArgumentException(
                $"Adjustment quantity is too small to record (minimum {1m / (decimal)Math.Pow(10, QuantityScale):0.###}).",
                nameof(quantity));
        }

        return rounded;
    }

    private static string? ValidateNotes(InventoryAdjustmentReason reason, string? notes)
    {
        var trimmed = Trim(notes);

        // Whitespace does not count as an explanation.
        if (reason.RequiresNotes() && trimmed is null)
        {
            throw new ArgumentException(
                $"Notes are required when the reason is '{reason.ToDisplayText()}'.", nameof(notes));
        }

        return trimmed;
    }

    private static decimal Round(decimal value) =>
        Math.Round(value, QuantityScale, MidpointRounding.AwayFromZero);

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
