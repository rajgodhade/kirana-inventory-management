using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Purchasing;

public sealed class PurchaseOrderService(
    IKiranaDbContext db,
    ISequenceGenerator sequenceGenerator,
    IAuditLogger auditLogger,
    IPermissionEnforcer permissionEnforcer,
    IPurchaseGstCalculationService purchaseGstCalculationService) : IPurchaseOrderService
{
    public async Task<PurchaseOrder> CreateDraftAsync(SavePurchaseOrderDraftRequest request, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(request.PerformedByUserId, cancellationToken);
        var priced = await ValidateAndPriceAsync(request, requireLines: false, cancellationToken);
        var number = await sequenceGenerator.NextAsync("PurchaseOrder", "PO", 6, cancellationToken);
        var order = new PurchaseOrder
        {
            PurchaseOrderNumber = number,
            OrderDateUtc = request.OrderDateUtc ?? DateTime.UtcNow,
            CreatedAtUtc = DateTime.UtcNow,
            CreatedByUserId = request.PerformedByUserId,
            Status = PurchaseOrderStatus.Draft,
        };
        ApplyDraft(order, request, priced);
        db.PurchaseOrders.Add(order);
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(request.PerformedByUserId, "PurchaseOrderCreated", nameof(PurchaseOrder), order.Id.ToString(),
            newValue: $"{order.PurchaseOrderNumber} — ₹{order.GrandTotal:0.00}", cancellationToken: cancellationToken);
        return order;
    }

    public async Task<PurchaseOrder> UpdateDraftAsync(int purchaseOrderId, SavePurchaseOrderDraftRequest request, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(request.PerformedByUserId, cancellationToken);
        var order = await LoadTrackedAsync(purchaseOrderId, cancellationToken);
        EnsureDraft(order);
        var previous = $"Supplier={order.SupplierCodeSnapshot}; Items={order.Items.Count}; Total={order.GrandTotal:0.00}";
        var priced = await ValidateAndPriceAsync(request, requireLines: false, cancellationToken);
        db.PurchaseOrderItems.RemoveRange(order.Items);
        order.Items.Clear();
        ApplyDraft(order, request, priced);
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(request.PerformedByUserId, "PurchaseOrderDraftUpdated", nameof(PurchaseOrder), order.Id.ToString(),
            previousValue: previous,
            newValue: $"Supplier={order.SupplierCodeSnapshot}; Items={order.Items.Count}; Total={order.GrandTotal:0.00}",
            cancellationToken: cancellationToken);
        return order;
    }

    public async Task<PurchaseOrder> SubmitAsync(int purchaseOrderId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(performedByUserId, cancellationToken);
        var order = await LoadTrackedAsync(purchaseOrderId, cancellationToken);
        EnsureDraft(order);
        if (order.Items.Count == 0) throw new InvalidOperationException("Add at least one item before submitting the order.");

        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == order.SupplierId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");
        if (!supplier.IsActive) throw new InvalidOperationException($"'{supplier.Name}' is inactive and cannot receive new orders.");

        var productIds = order.Items.Select(i => i.ProductId).Distinct().ToList();
        var activeProductIds = await db.Products.AsNoTracking()
            .Where(p => productIds.Contains(p.Id) && p.IsActive).Select(p => p.Id).ToListAsync(cancellationToken);
        if (activeProductIds.Count != productIds.Count)
            throw new InvalidOperationException("One or more products are inactive or no longer available.");

        order.Status = PurchaseOrderStatus.Submitted;
        order.SubmittedAtUtc = DateTime.UtcNow;
        order.SubmittedByUserId = performedByUserId;
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(performedByUserId, "PurchaseOrderSubmitted", nameof(PurchaseOrder), order.Id.ToString(),
            newValue: order.PurchaseOrderNumber, cancellationToken: cancellationToken);
        return order;
    }

    public async Task<PurchaseOrder> CancelAsync(CancelPurchaseOrderRequest request, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(request.PerformedByUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("Cancellation reason is required.", nameof(request));
        var order = await LoadTrackedAsync(request.PurchaseOrderId, cancellationToken);
        if (order.Status is not (PurchaseOrderStatus.Draft or PurchaseOrderStatus.Submitted))
            throw new InvalidOperationException($"A {order.Status} purchase order cannot be cancelled.");
        var previousStatus = order.Status;
        order.Status = PurchaseOrderStatus.Cancelled;
        order.CancelledAtUtc = DateTime.UtcNow;
        order.CancelledByUserId = request.PerformedByUserId;
        order.CancellationReason = request.Reason.Trim();
        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(request.PerformedByUserId, "PurchaseOrderCancelled", nameof(PurchaseOrder), order.Id.ToString(),
            previousValue: previousStatus.ToString(), newValue: PurchaseOrderStatus.Cancelled.ToString(), reason: order.CancellationReason,
            cancellationToken: cancellationToken);
        return order;
    }

    public async Task<PurchaseOrder?> GetByIdAsync(int purchaseOrderId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(performedByUserId, cancellationToken);
        return await BaseQuery().FirstOrDefaultAsync(p => p.Id == purchaseOrderId, cancellationToken);
    }

    public async Task<IReadOnlyList<PurchaseOrder>> SearchAsync(PurchaseOrderSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await EnsurePermissionAsync(performedByUserId, cancellationToken);
        var source = BaseQuery();
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var pattern = $"%{query.SearchText.Trim()}%";
            source = source.Where(p => EF.Functions.Like(p.PurchaseOrderNumber, pattern)
                || EF.Functions.Like(p.SupplierNameSnapshot, pattern)
                || EF.Functions.Like(p.SupplierCodeSnapshot, pattern));
        }
        if (query.Status is { } status) source = source.Where(p => p.Status == status);
        if (query.FromUtc is { } from) source = source.Where(p => p.OrderDateUtc >= from);
        if (query.ToUtc is { } to) source = source.Where(p => p.OrderDateUtc <= to);
        source = query.Sort switch
        {
            PurchaseOrderSort.Oldest => source.OrderBy(p => p.OrderDateUtc),
            PurchaseOrderSort.Number => source.OrderBy(p => p.PurchaseOrderNumber),
            PurchaseOrderSort.Supplier => source.OrderBy(p => p.SupplierNameSnapshot),
            PurchaseOrderSort.HighestTotal => source.OrderByDescending(p => p.GrandTotal),
            _ => source.OrderByDescending(p => p.OrderDateUtc),
        };
        return await source.Take(query.MaxResults).ToListAsync(cancellationToken);
    }

    private IQueryable<PurchaseOrder> BaseQuery() => db.PurchaseOrders.AsNoTracking()
        .Include(p => p.Items).Include(p => p.CreatedByUser).Include(p => p.SubmittedByUser).Include(p => p.CancelledByUser);

    private Task EnsurePermissionAsync(int? userId, CancellationToken cancellationToken) =>
        permissionEnforcer.EnsureHasPermissionAsync(userId, PermissionKeys.PurchasesManage, cancellationToken);

    private async Task<PurchaseOrder> LoadTrackedAsync(int id, CancellationToken cancellationToken) =>
        await db.PurchaseOrders.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
        ?? throw new InvalidOperationException("Purchase order not found.");

    private static void EnsureDraft(PurchaseOrder order)
    {
        if (order.Status != PurchaseOrderStatus.Draft)
            throw new InvalidOperationException("Only draft purchase orders can be edited or submitted.");
    }

    private async Task<PricedDraft> ValidateAndPriceAsync(SavePurchaseOrderDraftRequest request, bool requireLines, CancellationToken cancellationToken)
    {
        if (requireLines && request.Lines.Count == 0) throw new ArgumentException("A purchase order must have at least one item.");
        if (request.Lines.Select(l => l.ProductId).Distinct().Count() != request.Lines.Count)
            throw new ArgumentException("A product can appear only once in a purchase order.");
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(s => s.Id == request.SupplierId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier not found.");
        if (!supplier.IsActive) throw new InvalidOperationException($"'{supplier.Name}' is inactive and cannot receive new orders.");

        var ids = request.Lines.Select(l => l.ProductId).ToList();
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var priceLines = new List<PurchaseLine>();
        foreach (var input in request.Lines)
        {
            if (!products.TryGetValue(input.ProductId, out var product)) throw new InvalidOperationException($"Product #{input.ProductId} was not found.");
            if (!product.IsActive) throw new InvalidOperationException($"'{product.Name}' is inactive and cannot be ordered.");
            if (input.OrderedQuantity <= 0) throw new ArgumentException($"Quantity for '{product.Name}' must be greater than zero.");
            if (!product.Unit.SupportsDecimalQuantity() && input.OrderedQuantity != Math.Floor(input.OrderedQuantity))
                throw new ArgumentException($"'{product.Name}' is ordered in whole {product.Unit} units.");
            priceLines.Add(new PurchaseLine
            {
                ProductId = product.Id, Quantity = input.OrderedQuantity, UnitPrice = input.UnitCost,
                DiscountPercent = input.DiscountPercent, PricingType = input.PricingType ?? product.PricingType,
                GstRatePercent = product.GstRatePercent ?? 0,
            });
        }
        var totals = purchaseGstCalculationService.Calculate(priceLines);
        return new PricedDraft(supplier, products, totals);
    }

    private static void ApplyDraft(PurchaseOrder order, SavePurchaseOrderDraftRequest request, PricedDraft priced)
    {
        order.SupplierId = priced.Supplier.Id;
        order.SupplierNameSnapshot = priced.Supplier.Name;
        order.SupplierCodeSnapshot = priced.Supplier.SupplierCode;
        order.SupplierContactSnapshot = priced.Supplier.Phone ?? priced.Supplier.Email ?? priced.Supplier.ContactPerson;
        order.OrderDateUtc = request.OrderDateUtc ?? order.OrderDateUtc;
        order.Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim();
        order.SubTotal = priced.Totals.SubTotal;
        order.DiscountTotal = priced.Totals.DiscountTotal;
        order.TaxableTotal = priced.Totals.TaxableTotal;
        order.TaxTotal = priced.Totals.TaxTotal;
        order.RoundOffAmount = priced.Totals.RoundOffAmount;
        order.GrandTotal = priced.Totals.GrandTotal;
        foreach (var result in priced.Totals.Lines)
        {
            var product = priced.Products[result.Line.ProductId];
            order.Items.Add(new PurchaseOrderItem
            {
                ProductId = product.Id, ProductNameSnapshot = product.Name, ProductCodeSnapshot = product.ProductCode,
                SkuSnapshot = product.Sku, HsnCodeSnapshot = product.HsnCode, UnitSnapshot = product.Unit.ToString(),
                PricingTypeSnapshot = result.Line.PricingType, GstRatePercentSnapshot = result.Line.GstRatePercent,
                OrderedQuantity = result.Line.Quantity, UnitCost = result.Line.UnitPrice,
                DiscountPercent = result.Line.DiscountPercent, DiscountAmount = result.DiscountAmount,
                TaxableAmount = result.TaxableAmount, GstAmount = result.GstAmount, LineTotal = result.LineTotal,
            });
        }
    }

    private sealed record PricedDraft(Supplier Supplier, IReadOnlyDictionary<int, Product> Products, PurchaseTotals Totals);
}
