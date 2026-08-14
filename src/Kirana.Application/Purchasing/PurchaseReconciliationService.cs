using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Purchasing;

/// <summary>
/// Derives procurement control information from authoritative documents. This service deliberately
/// has no audit logger and never calls SaveChanges: opening or refreshing reconciliation cannot
/// mutate inventory, payables, documents, status, or audit history.
/// </summary>
public sealed class PurchaseReconciliationService(
    IKiranaDbContext db,
    IPermissionEnforcer permissionEnforcer) : IPurchaseReconciliationService
{
    private const decimal QuantityTolerance = 0.0001m;
    private const decimal MoneyTolerance = 0.02m;

    public async Task<PurchaseReconciliationResult> SearchAsync(
        PurchaseReconciliationQuery query,
        int? performedByUserId,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(
            performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        var calculatedAtUtc = DateTime.UtcNow;
        var source = db.PurchaseOrders.AsNoTracking()
            .Include(x => x.Items)
            .Where(x => x.Status != PurchaseOrderStatus.Draft && x.Status != PurchaseOrderStatus.Cancelled);

        if (query.PurchaseOrderId is { } purchaseOrderId)
            source = source.Where(x => x.Id == purchaseOrderId);
        if (query.SupplierId is { } supplierId)
            source = source.Where(x => x.SupplierId == supplierId);
        if (query.FromUtc is { } from)
            source = source.Where(x => x.OrderDateUtc >= from);
        if (query.ToUtc is { } to)
            source = source.Where(x => x.OrderDateUtc <= to);

        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var pattern = $"%{query.SearchText.Trim()}%";
            source = source.Where(x =>
                EF.Functions.Like(x.PurchaseOrderNumber, pattern)
                || EF.Functions.Like(x.SupplierNameSnapshot, pattern)
                || EF.Functions.Like(x.SupplierCodeSnapshot, pattern)
                || db.GoodsReceipts.Any(g => g.PurchaseOrderId == x.Id
                    && EF.Functions.Like(g.GoodsReceiptNumber, pattern))
                || db.Purchases.Any(p => p.PurchaseOrderId == x.Id
                    && (EF.Functions.Like(p.PurchaseNumber, pattern)
                        || (p.SupplierInvoiceNumber != null && EF.Functions.Like(p.SupplierInvoiceNumber, pattern)))));
        }

        // Derived filters must be evaluated after aggregation. The bounded candidate set avoids an
        // unbounded desktop query while still applying the user's stable DB-side filters first.
        var orders = await source.OrderByDescending(x => x.OrderDateUtc)
            .Take(Math.Max(query.MaxResults * 4, 2000))
            .ToListAsync(cancellationToken);
        var orderIds = orders.Select(x => x.Id).ToList();

        var receipts = orderIds.Count == 0
            ? []
            : await db.GoodsReceipts.AsNoTracking().Include(x => x.Items)
                .Where(x => orderIds.Contains(x.PurchaseOrderId))
                .ToListAsync(cancellationToken);
        var purchases = orderIds.Count == 0
            ? []
            : await db.Purchases.AsNoTracking().Include(x => x.Items)
                .Where(x => x.PurchaseOrderId != null && orderIds.Contains(x.PurchaseOrderId.Value))
                .ToListAsync(cancellationToken);

        var receiptsByOrder = receipts.ToLookup(x => x.PurchaseOrderId);
        var purchasesByOrder = purchases.ToLookup(x => x.PurchaseOrderId!.Value);
        var records = orders.Select(order => BuildRecord(
            order,
            receiptsByOrder[order.Id].ToList(),
            purchasesByOrder[order.Id].ToList(),
            calculatedAtUtc)).ToList();

        var metrics = BuildMetrics(records);
        IEnumerable<PurchaseReconciliationRecord> filtered = records.Where(x => Matches(x, query.Filter));
        filtered = query.Sort switch
        {
            PurchaseReconciliationSort.Oldest => filtered.OrderBy(x => x.OrderDateUtc),
            PurchaseReconciliationSort.Supplier => filtered.OrderBy(x => x.SupplierName),
            PurchaseReconciliationSort.HighestVariance => filtered.OrderByDescending(x => Math.Abs(x.TotalVariance)),
            _ => filtered.OrderByDescending(x => x.OrderDateUtc),
        };

        return new PurchaseReconciliationResult(
            filtered.Take(query.MaxResults).ToList(), metrics, calculatedAtUtc);
    }

    public async Task<PurchaseReconciliationRecord?> GetByPurchaseOrderIdAsync(
        int purchaseOrderId,
        int? performedByUserId,
        CancellationToken cancellationToken = default) =>
        (await SearchAsync(new PurchaseReconciliationQuery
        {
            PurchaseOrderId = purchaseOrderId,
            MaxResults = 1,
        }, performedByUserId, cancellationToken)).Records.SingleOrDefault();

    private static PurchaseReconciliationRecord BuildRecord(
        PurchaseOrder order,
        IReadOnlyList<GoodsReceipt> receipts,
        IReadOnlyList<Purchase> purchases,
        DateTime calculatedAtUtc)
    {
        var completedReceiptItems = receipts.Where(x => x.Status == GoodsReceiptStatus.Completed)
            .SelectMany(x => x.Items).ToList();
        var purchaseItems = purchases.Where(x => x.Status == PurchaseStatus.Completed)
            .SelectMany(x => x.Items).ToList();
        var lines = new List<PurchaseReconciliationLine>();

        foreach (var item in order.Items.OrderBy(x => x.Id))
        {
            var received = completedReceiptItems.Where(x => x.PurchaseOrderItemId == item.Id)
                .Sum(x => x.ReceivedQuantity);
            var actualItems = purchaseItems.Where(x => x.ProductId == item.ProductId).ToList();
            var purchased = actualItems.Sum(x => x.Quantity);
            var actualGross = actualItems.Sum(x => x.Quantity * x.PurchasePriceSnapshot);
            var actualUnitCost = purchased > QuantityTolerance ? actualGross / purchased : null as decimal?;
            var unitVariance = actualUnitCost is { } actual ? actual - item.UnitCost : null as decimal?;
            var unitVariancePercent = unitVariance is { } difference && item.UnitCost != 0
                ? difference / item.UnitCost * 100m
                : null as decimal?;
            var expectedTotal = item.LineTotal;
            var actualTotal = actualItems.Sum(x => x.LineTotal);
            var expectedTax = item.GstAmount;
            var actualTax = actualItems.Sum(x => x.GstAmount);
            var expectedDiscount = item.DiscountAmount;
            var actualDiscount = actualItems.Sum(x => x.DiscountAmount);
            var pendingReceipt = Positive(item.OrderedQuantity - received);
            var pendingInvoice = Positive(received - purchased);
            var overReceived = Positive(received - item.OrderedQuantity);
            var overInvoiced = Positive(purchased - received);

            var flags = PurchaseReconciliationFlags.None;
            if (!Equal(item.OrderedQuantity, received) || !Equal(received, purchased))
                flags |= PurchaseReconciliationFlags.QuantityMismatch;
            if (unitVariance is { } priceVariance && Math.Abs(priceVariance) > MoneyTolerance)
                flags |= PurchaseReconciliationFlags.PriceMismatch;

            // Compare tax stored by both authoritative documents. For a partial invoice, scale the
            // PO line tax to the purchased quantity so missing quantity is not mislabeled as a tax
            // calculation error; the full-value tax variance remains visible in the displayed data.
            var comparableExpectedTax = item.OrderedQuantity > QuantityTolerance
                ? item.GstAmount * purchased / item.OrderedQuantity
                : 0m;
            if (purchased > QuantityTolerance && Math.Abs(actualTax - comparableExpectedTax) > MoneyTolerance)
                flags |= PurchaseReconciliationFlags.TaxMismatch;
            if (overReceived > QuantityTolerance)
                flags |= PurchaseReconciliationFlags.OverReceived | PurchaseReconciliationFlags.Exception;
            if (overInvoiced > QuantityTolerance)
                flags |= PurchaseReconciliationFlags.OverInvoiced | PurchaseReconciliationFlags.Exception;

            lines.Add(new PurchaseReconciliationLine(
                item.Id, item.ProductId, item.ProductNameSnapshot, item.ProductCodeSnapshot,
                item.SkuSnapshot, item.UnitSnapshot, item.OrderedQuantity, received, purchased,
                pendingReceipt, pendingInvoice, overReceived, overInvoiced, item.UnitCost,
                actualUnitCost, unitVariance, unitVariancePercent, expectedTotal, actualTotal,
                actualTotal - expectedTotal, expectedDiscount, actualDiscount,
                actualDiscount - expectedDiscount, expectedTax, actualTax, actualTax - expectedTax,
                flags));
        }

        var recordFlags = lines.Aggregate(PurchaseReconciliationFlags.None, (current, line) => current | line.Flags);
        if ((recordFlags & (PurchaseReconciliationFlags.PriceMismatch
            | PurchaseReconciliationFlags.TaxMismatch
            | PurchaseReconciliationFlags.OverInvoiced
            | PurchaseReconciliationFlags.OverReceived)) != 0)
            recordFlags |= PurchaseReconciliationFlags.Exception;
        var anyReceived = lines.Any(x => x.ReceivedQuantity > QuantityTolerance);
        var allReceived = lines.All(x => Equal(x.OrderedQuantity, x.ReceivedQuantity));
        var anyPurchased = lines.Any(x => x.PurchasedQuantity > QuantityTolerance);
        var allInvoiced = lines.All(x => Equal(x.ReceivedQuantity, x.PurchasedQuantity));
        if (!anyReceived) recordFlags |= PurchaseReconciliationFlags.AwaitingReceipt;
        else if (!allReceived) recordFlags |= PurchaseReconciliationFlags.PartiallyReceived;
        if (anyReceived && !anyPurchased) recordFlags |= PurchaseReconciliationFlags.AwaitingPurchase;
        if (lines.Any(x => x.PendingInvoiceQuantity > QuantityTolerance))
            recordFlags |= PurchaseReconciliationFlags.PendingPurchase;
        if (allReceived && allInvoiced
            && (recordFlags & (PurchaseReconciliationFlags.PriceMismatch
                | PurchaseReconciliationFlags.TaxMismatch
                | PurchaseReconciliationFlags.Exception)) == 0)
            recordFlags |= PurchaseReconciliationFlags.FullyReconciled;

        return new PurchaseReconciliationRecord
        {
            PurchaseOrderId = order.Id,
            PurchaseOrderNumber = order.PurchaseOrderNumber,
            SupplierId = order.SupplierId,
            SupplierName = order.SupplierNameSnapshot,
            SupplierCode = order.SupplierCodeSnapshot,
            OrderDateUtc = order.OrderDateUtc,
            PurchaseOrderStatus = order.Status,
            CalculatedAtUtc = calculatedAtUtc,
            Lines = lines,
            Flags = recordFlags,
            ActualValue = purchases.Where(x => x.Status == PurchaseStatus.Completed).Sum(x => x.GrandTotal),
            GoodsReceipts = receipts.OrderBy(x => x.ReceivedAtUtc).Select(x =>
                new PurchaseReconciliationDocument(x.Id, x.GoodsReceiptNumber, x.ReceivedAtUtc,
                    x.Status.ToString(), x.Items.Sum(i => i.ReceivedQuantity))).ToList(),
            Purchases = purchases.OrderBy(x => x.PurchaseDateUtc).Select(x =>
                new PurchaseReconciliationDocument(x.Id, x.PurchaseNumber, x.PurchaseDateUtc,
                    x.Status.ToString(), x.Items.Sum(i => i.Quantity))).ToList(),
        };
    }

    private static PurchaseReconciliationMetrics BuildMetrics(IReadOnlyList<PurchaseReconciliationRecord> records) =>
        new(
            records.Count,
            records.Count(x => x.Has(PurchaseReconciliationFlags.FullyReconciled)),
            records.Count(x => x.PendingReceiptQuantity > QuantityTolerance),
            records.Count(x => x.PendingInvoiceQuantity > QuantityTolerance),
            records.Count(x => x.Has(PurchaseReconciliationFlags.QuantityMismatch)),
            records.Count(x => x.Has(PurchaseReconciliationFlags.PriceMismatch)),
            records.Count(x => x.Has(PurchaseReconciliationFlags.TaxMismatch)),
            records.Count(x => x.Has(PurchaseReconciliationFlags.Exception)),
            records.Sum(x => x.ExpectedValue),
            records.Sum(x => x.ActualValue),
            records.Sum(x => x.TotalVariance));

    private static bool Matches(PurchaseReconciliationRecord record, PurchaseReconciliationFilter filter) => filter switch
    {
        PurchaseReconciliationFilter.FullyReconciled => record.Has(PurchaseReconciliationFlags.FullyReconciled),
        PurchaseReconciliationFilter.PendingReceipt => record.PendingReceiptQuantity > QuantityTolerance,
        PurchaseReconciliationFilter.PendingPurchase => record.PendingInvoiceQuantity > QuantityTolerance,
        PurchaseReconciliationFilter.QuantityMismatch => record.Has(PurchaseReconciliationFlags.QuantityMismatch),
        PurchaseReconciliationFilter.PriceMismatch => record.Has(PurchaseReconciliationFlags.PriceMismatch),
        PurchaseReconciliationFilter.TaxMismatch => record.Has(PurchaseReconciliationFlags.TaxMismatch),
        PurchaseReconciliationFilter.Exceptions => record.Has(PurchaseReconciliationFlags.Exception),
        _ => true,
    };

    private static decimal Positive(decimal value) => value > QuantityTolerance ? value : 0m;
    private static bool Equal(decimal left, decimal right) => Math.Abs(left - right) <= QuantityTolerance;
}
