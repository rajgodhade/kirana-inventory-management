using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Purchasing;

/// <summary>Records physical receipt without posting stock or supplier payable. Completion re-reads
/// completed quantities inside a transaction so stale screens cannot over-receive a PO.</summary>
public sealed class GoodsReceiptService(
    IKiranaDbContext db,
    ISequenceGenerator sequenceGenerator,
    IAuditLogger auditLogger,
    IPermissionEnforcer permissionEnforcer) : IGoodsReceiptService
{
    public async Task<PurchaseOrderReceiptPreview> GetReceiptPreviewAsync(
        int purchaseOrderId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(performedByUserId, cancellationToken);
        return await BuildPreviewAsync(purchaseOrderId, cancellationToken);
    }

    public async Task<GoodsReceipt> CreateDraftAsync(
        CreateGoodsReceiptDraftRequest request, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(request.PerformedByUserId, cancellationToken);
        if (request.Lines.Count == 0 || request.Lines.All(l => l.ReceivedQuantity == 0))
            throw new ArgumentException("Enter a positive received quantity for at least one item.", nameof(request));
        if (request.Lines.Select(l => l.PurchaseOrderItemId).Distinct().Count() != request.Lines.Count)
            throw new ArgumentException("A purchase order item can appear only once in a goods receipt.", nameof(request));

        var preview = await BuildPreviewAsync(request.PurchaseOrderId, cancellationToken);
        EnsureReceivable(preview.Status);
        var previewLines = preview.Lines.ToDictionary(l => l.PurchaseOrderItemId);
        var orderItems = await db.PurchaseOrderItems.AsNoTracking()
            .Where(i => i.PurchaseOrderId == request.PurchaseOrderId)
            .ToDictionaryAsync(i => i.Id, cancellationToken);

        // Validate the complete draft before consuming a document number. A rejected draft must
        // not leave a rolled-back sequence entity tracked in this long-lived desktop DbContext.
        var preparedLines = new List<(GoodsReceiptLineInput Input, PurchaseOrderReceiptLine Preview,
            PurchaseOrderItem OrderItem, string? Barcode)>();
        foreach (var input in request.Lines.Where(l => l.ReceivedQuantity != 0))
        {
            if (!previewLines.TryGetValue(input.PurchaseOrderItemId, out var line)
                || !orderItems.TryGetValue(input.PurchaseOrderItemId, out var orderItem))
                throw new InvalidOperationException("A selected item does not belong to this purchase order.");

            ValidateQuantity(line, input.ReceivedQuantity);
            var barcode = await ValidateBarcodeAsync(line.ProductId, input.Barcode, cancellationToken);
            preparedLines.Add((input, line, orderItem, barcode));
        }

        if (preparedLines.Count == 0)
            throw new ArgumentException("Enter a positive received quantity for at least one item.", nameof(request));

        var supplierCodeSnapshot = await db.PurchaseOrders.AsNoTracking()
            .Where(p => p.Id == preview.PurchaseOrderId)
            .Select(p => p.SupplierCodeSnapshot)
            .SingleAsync(cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var receipt = new GoodsReceipt
            {
                GoodsReceiptNumber = await sequenceGenerator.NextAsync("GoodsReceipt", "GRN", 6, cancellationToken),
                PurchaseOrderId = preview.PurchaseOrderId,
                SupplierId = preview.SupplierId,
                SupplierNameSnapshot = preview.SupplierName,
                SupplierCodeSnapshot = supplierCodeSnapshot,
                ReceivedAtUtc = request.ReceivedAtUtc ?? DateTime.UtcNow,
                Status = GoodsReceiptStatus.Draft,
                Notes = Trim(request.Notes),
                CreatedByUserId = request.PerformedByUserId,
            };

            foreach (var prepared in preparedLines)
            {
                var input = prepared.Input;
                var line = prepared.Preview;
                var orderItem = prepared.OrderItem;
                receipt.Items.Add(new GoodsReceiptItem
                {
                    PurchaseOrderItemId = line.PurchaseOrderItemId,
                    ProductId = line.ProductId,
                    ProductNameSnapshot = orderItem.ProductNameSnapshot,
                    ProductCodeSnapshot = orderItem.ProductCodeSnapshot,
                    SkuSnapshot = orderItem.SkuSnapshot,
                    UnitSnapshot = line.Unit,
                    OrderedQuantitySnapshot = line.OrderedQuantity,
                    ReceivedQuantity = RoundQuantity(input.ReceivedQuantity),
                    BarcodeSnapshot = prepared.Barcode,
                    Notes = Trim(input.Notes),
                });
            }

            db.GoodsReceipts.Add(receipt);
            await db.SaveChangesAsync(cancellationToken);
            await auditLogger.RecordAsync(request.PerformedByUserId, "GoodsReceiptCreated", nameof(GoodsReceipt), receipt.Id.ToString(),
                newValue: Describe(receipt, preview.PurchaseOrderNumber), cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return receipt;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<GoodsReceipt> CompleteAsync(
        int goodsReceiptId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(performedByUserId, cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var authoritative = await db.GoodsReceipts.AsNoTracking().Include(g => g.Items)
                .FirstOrDefaultAsync(g => g.Id == goodsReceiptId, cancellationToken)
                ?? throw new InvalidOperationException("Goods receipt not found.");
            if (authoritative.Status != GoodsReceiptStatus.Draft)
                throw new InvalidOperationException("Only a draft goods receipt can be completed.");

            var order = await db.PurchaseOrders.Include(p => p.Items)
                .FirstOrDefaultAsync(p => p.Id == authoritative.PurchaseOrderId, cancellationToken)
                ?? throw new InvalidOperationException("Purchase order not found.");
            EnsureReceivable(order.Status);

            var priorReceived = await db.GoodsReceiptItems.AsNoTracking()
                .Where(i => i.GoodsReceipt.PurchaseOrderId == order.Id
                    && i.GoodsReceipt.Status == GoodsReceiptStatus.Completed
                    && i.GoodsReceiptId != goodsReceiptId)
                .GroupBy(i => i.PurchaseOrderItemId)
                .Select(g => new { ItemId = g.Key, Quantity = g.Sum(i => i.ReceivedQuantity) })
                .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, cancellationToken);

            foreach (var item in authoritative.Items)
            {
                var orderItem = order.Items.FirstOrDefault(i => i.Id == item.PurchaseOrderItemId)
                    ?? throw new InvalidOperationException("The purchase order item no longer exists.");
                var alreadyReceived = priorReceived.GetValueOrDefault(orderItem.Id);
                var remaining = orderItem.OrderedQuantity - alreadyReceived;
                if (item.ReceivedQuantity > remaining)
                    throw new InvalidOperationException(
                        $"Cannot receive {item.ReceivedQuantity:0.###} {item.UnitSnapshot} of '{item.ProductNameSnapshot}'. " +
                        $"Only {Math.Max(0, remaining):0.###} remains on {order.PurchaseOrderNumber}.");
            }

            var tracked = await db.GoodsReceipts.Include(g => g.Items)
                .FirstAsync(g => g.Id == goodsReceiptId, cancellationToken);
            tracked.Status = GoodsReceiptStatus.Completed;
            tracked.CompletedAtUtc = DateTime.UtcNow;
            tracked.CompletedByUserId = performedByUserId;

            var currentReceipt = authoritative.Items.ToDictionary(i => i.PurchaseOrderItemId, i => i.ReceivedQuantity);
            var fullyReceived = order.Items.All(i =>
                priorReceived.GetValueOrDefault(i.Id) + currentReceipt.GetValueOrDefault(i.Id) >= i.OrderedQuantity);
            order.Status = fullyReceived ? PurchaseOrderStatus.Completed : PurchaseOrderStatus.PartiallyReceived;
            order.UpdatedAtUtc = DateTime.UtcNow;

            await db.SaveChangesAsync(cancellationToken);
            await auditLogger.RecordAsync(performedByUserId, "GoodsReceiptCompleted", nameof(GoodsReceipt), tracked.Id.ToString(),
                previousValue: GoodsReceiptStatus.Draft.ToString(),
                newValue: Describe(tracked, order.PurchaseOrderNumber), cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return tracked;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<GoodsReceipt> CancelAsync(
        CancelGoodsReceiptRequest request, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(request.PerformedByUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Reason))
            throw new ArgumentException("Cancellation reason is required.", nameof(request));
        var receipt = await db.GoodsReceipts.Include(g => g.PurchaseOrder)
            .FirstOrDefaultAsync(g => g.Id == request.GoodsReceiptId, cancellationToken)
            ?? throw new InvalidOperationException("Goods receipt not found.");
        if (receipt.Status != GoodsReceiptStatus.Draft)
            throw new InvalidOperationException("Only a draft goods receipt can be cancelled.");
        receipt.Status = GoodsReceiptStatus.Cancelled;
        receipt.CancelledAtUtc = DateTime.UtcNow;
        receipt.CancelledByUserId = request.PerformedByUserId;
        receipt.CancellationReason = request.Reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(request.PerformedByUserId, "GoodsReceiptCancelled", nameof(GoodsReceipt), receipt.Id.ToString(),
            previousValue: GoodsReceiptStatus.Draft.ToString(), newValue: GoodsReceiptStatus.Cancelled.ToString(),
            reason: receipt.CancellationReason, cancellationToken: cancellationToken);
        return receipt;
    }

    public async Task<GoodsReceipt?> GetByIdAsync(
        int goodsReceiptId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(performedByUserId, cancellationToken);
        return await BaseQuery().FirstOrDefaultAsync(g => g.Id == goodsReceiptId, cancellationToken);
    }

    public async Task<IReadOnlyList<GoodsReceipt>> SearchAsync(
        GoodsReceiptSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(performedByUserId, cancellationToken);
        var source = BaseQuery();
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var pattern = $"%{query.SearchText.Trim()}%";
            source = source.Where(g => EF.Functions.Like(g.GoodsReceiptNumber, pattern)
                || EF.Functions.Like(g.PurchaseOrder.PurchaseOrderNumber, pattern)
                || EF.Functions.Like(g.SupplierNameSnapshot, pattern));
        }
        if (query.SupplierId is { } supplierId) source = source.Where(g => g.SupplierId == supplierId);
        if (query.Status is { } status) source = source.Where(g => g.Status == status);
        if (query.FromUtc is { } from) source = source.Where(g => g.ReceivedAtUtc >= from);
        if (query.ToUtc is { } to) source = source.Where(g => g.ReceivedAtUtc <= to);
        source = query.OldestFirst
            ? source.OrderBy(g => g.ReceivedAtUtc).ThenBy(g => g.Id)
            : source.OrderByDescending(g => g.ReceivedAtUtc).ThenByDescending(g => g.Id);
        return await source.Take(query.MaxResults).ToListAsync(cancellationToken);
    }

    public async Task<GoodsReceiptPurchasePrefill> GetPurchasePrefillAsync(
        int goodsReceiptId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(performedByUserId, cancellationToken);
        var receipt = await BaseQuery().FirstOrDefaultAsync(g => g.Id == goodsReceiptId, cancellationToken)
            ?? throw new InvalidOperationException("Goods receipt not found.");
        if (receipt.Status != GoodsReceiptStatus.Completed)
            throw new InvalidOperationException("Complete the goods receipt before creating a purchase.");
        if (receipt.Purchase is not null)
            throw new InvalidOperationException($"Purchase {receipt.Purchase.PurchaseNumber} already exists for this goods receipt.");
        var orderItems = receipt.PurchaseOrder.Items.ToDictionary(i => i.Id);
        return new GoodsReceiptPurchasePrefill(
            receipt.Id, receipt.GoodsReceiptNumber, receipt.PurchaseOrderId, receipt.PurchaseOrder.PurchaseOrderNumber,
            receipt.SupplierId,
            receipt.Items.Select(i =>
            {
                var orderItem = orderItems[i.PurchaseOrderItemId];
                return new PurchaseLineInput
                {
                    ProductId = i.ProductId,
                    Quantity = i.ReceivedQuantity,
                    UnitPrice = orderItem.UnitCost,
                    DiscountPercent = orderItem.DiscountPercent,
                    PricingType = orderItem.PricingTypeSnapshot,
                };
            }).ToList());
    }

    private async Task<PurchaseOrderReceiptPreview> BuildPreviewAsync(int orderId, CancellationToken cancellationToken)
    {
        var order = await db.PurchaseOrders.AsNoTracking().Include(p => p.Items)
            .FirstOrDefaultAsync(p => p.Id == orderId, cancellationToken)
            ?? throw new InvalidOperationException("Purchase order not found.");
        EnsureReceivable(order.Status);
        var received = await db.GoodsReceiptItems.AsNoTracking()
            .Where(i => i.GoodsReceipt.PurchaseOrderId == orderId && i.GoodsReceipt.Status == GoodsReceiptStatus.Completed)
            .GroupBy(i => i.PurchaseOrderItemId)
            .Select(g => new { ItemId = g.Key, Quantity = g.Sum(i => i.ReceivedQuantity) })
            .ToDictionaryAsync(x => x.ItemId, x => x.Quantity, cancellationToken);
        return new PurchaseOrderReceiptPreview(order.Id, order.PurchaseOrderNumber, order.SupplierId,
            order.SupplierNameSnapshot, order.OrderDateUtc, order.Status,
            order.Items.Select(i =>
            {
                var unit = Enum.TryParse<UnitOfMeasure>(i.UnitSnapshot, out var parsed) ? parsed : UnitOfMeasure.Piece;
                var prior = received.GetValueOrDefault(i.Id);
                return new PurchaseOrderReceiptLine(i.Id, i.ProductId, i.ProductNameSnapshot, i.ProductCodeSnapshot,
                    unit, i.OrderedQuantity, prior, Math.Max(0, i.OrderedQuantity - prior), i.UnitCost,
                    i.DiscountPercent, i.PricingTypeSnapshot);
            }).ToList());
    }

    private IQueryable<GoodsReceipt> BaseQuery() => db.GoodsReceipts.AsNoTracking()
        .Include(g => g.Items).Include(g => g.PurchaseOrder).ThenInclude(p => p.Items)
        .Include(g => g.Purchase).Include(g => g.CreatedByUser).Include(g => g.CompletedByUser).Include(g => g.CancelledByUser);

    private async Task<string?> ValidateBarcodeAsync(int productId, string? barcode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(barcode)) return null;
        var normalized = BarcodeNormalizer.Normalize(barcode);
        var match = await db.ProductBarcodes.AsNoTracking()
            .FirstOrDefaultAsync(b => b.NormalizedValue == normalized, cancellationToken);
        if (match is null || !match.IsActive)
            throw new InvalidOperationException($"Barcode '{barcode.Trim()}' is unknown or retired.");
        if (match.ProductId != productId)
            throw new InvalidOperationException($"Barcode '{barcode.Trim()}' belongs to a different product.");
        return match.Value;
    }

    private static void ValidateQuantity(PurchaseOrderReceiptLine line, decimal quantity)
    {
        quantity = RoundQuantity(quantity);
        if (quantity <= 0) throw new ArgumentException($"Received quantity for '{line.ProductName}' must be greater than zero.");
        if (!line.Unit.SupportsDecimalQuantity() && quantity != Math.Floor(quantity))
            throw new ArgumentException($"'{line.ProductName}' is received in whole {line.Unit} units.");
        if (quantity > line.RemainingQuantity)
            throw new InvalidOperationException($"Cannot receive {quantity:0.###}; only {line.RemainingQuantity:0.###} remains for '{line.ProductName}'.");
    }

    private static void EnsureReceivable(PurchaseOrderStatus status)
    {
        if (status is not (PurchaseOrderStatus.Submitted or PurchaseOrderStatus.PartiallyReceived))
            throw new InvalidOperationException($"A {status} purchase order cannot receive goods.");
    }

    private Task EnsurePermissionAsync(int? userId, CancellationToken cancellationToken) =>
        permissionEnforcer.EnsureHasPermissionAsync(userId, PermissionKeys.PurchasesManage, cancellationToken);

    private static decimal RoundQuantity(decimal value) => Math.Round(value, 3, MidpointRounding.AwayFromZero);
    private static string? Trim(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string Describe(GoodsReceipt receipt, string purchaseOrderNumber) =>
        $"{receipt.GoodsReceiptNumber}; PO={purchaseOrderNumber}; Supplier={receipt.SupplierCodeSnapshot}; " +
        string.Join(", ", receipt.Items.Select(i => $"{i.ProductCodeSnapshot}={i.ReceivedQuantity:0.###} {i.UnitSnapshot}"));
}
