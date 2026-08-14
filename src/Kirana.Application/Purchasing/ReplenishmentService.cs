using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Purchasing;

/// <summary>Derives replenishment from authoritative inventory and existing procurement records.
/// This service deliberately has no audit logger and never calls SaveChanges.</summary>
public sealed class ReplenishmentService(IKiranaDbContext db, IPermissionEnforcer permissionEnforcer)
    : IReplenishmentService
{
    public async Task<ReplenishmentSummary> GetRecommendationsAsync(
        ReplenishmentQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(
            performedByUserId, PermissionKeys.PurchasesManage, cancellationToken);

        var products = await db.Products.AsNoTracking()
            .Include(p => p.Inventory)
            .Include(p => p.PreferredSupplier)
            .Where(p => p.IsActive)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        var openOrderLines = await db.PurchaseOrderItems.AsNoTracking()
            .Where(i => i.PurchaseOrder.Status == PurchaseOrderStatus.Submitted
                     || i.PurchaseOrder.Status == PurchaseOrderStatus.PartiallyReceived)
            .Select(i => new { i.Id, i.ProductId, i.OrderedQuantity })
            .ToListAsync(cancellationToken);

        var receivedByOrderLine = await db.GoodsReceiptItems.AsNoTracking()
            .Where(i => i.GoodsReceipt.Status == GoodsReceiptStatus.Completed)
            .GroupBy(i => i.PurchaseOrderItemId)
            .Select(g => new { PurchaseOrderItemId = g.Key, Quantity = g.Sum(i => i.ReceivedQuantity) })
            .ToDictionaryAsync(x => x.PurchaseOrderItemId, x => x.Quantity, cancellationToken);

        var openByProduct = openOrderLines
            .GroupBy(i => i.ProductId)
            .ToDictionary(
                g => g.Key,
                g => g.Sum(i => Math.Max(i.OrderedQuantity
                    - receivedByOrderLine.GetValueOrDefault(i.Id), 0)));

        var purchaseCosts = await db.PurchaseItems.AsNoTracking()
            .Where(i => i.Purchase.Status == PurchaseStatus.Completed)
            .OrderByDescending(i => i.Purchase.PurchaseDateUtc)
            .ThenByDescending(i => i.Id)
            .Select(i => new { i.ProductId, i.PurchasePriceSnapshot })
            .ToListAsync(cancellationToken);
        var latestCostByProduct = purchaseCosts
            .GroupBy(i => i.ProductId)
            .ToDictionary(g => g.Key, g => g.First().PurchasePriceSnapshot);

        var all = products.Select(product => Build(
            product,
            openByProduct.GetValueOrDefault(product.Id),
            latestCostByProduct.TryGetValue(product.Id, out var cost) ? cost : null)).ToList();

        var unconfiguredLowStock = all.Count(x => x.Status == ReplenishmentStatus.NotConfigured
            && x.ReorderLevel > 0 && x.CurrentStock <= x.ReorderLevel);

        IEnumerable<ReplenishmentRecommendation> filtered = all;
        if (!string.IsNullOrWhiteSpace(query.SearchText))
        {
            var text = query.SearchText.Trim();
            filtered = filtered.Where(x => x.ProductName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || x.ProductCode.Contains(text, StringComparison.OrdinalIgnoreCase));
        }
        if (query.SupplierId is { } supplierId)
            filtered = filtered.Where(x => x.PreferredSupplierId == supplierId);
        if (query.Status is { } status)
            filtered = filtered.Where(x => x.Status == status);
        if (query.Enabled is { } enabled)
            filtered = filtered.Where(x => x.IsConfigured == enabled);
        if (query.NeedsReorderOnly)
            filtered = filtered.Where(x => x.IsConfigured
                && x.Status is ReplenishmentStatus.AtReorderLevel
                    or ReplenishmentStatus.BelowReorderLevel
                    or ReplenishmentStatus.OutOfStock
                && x.SuggestedQuantity > 0);

        var items = filtered.ToList();
        return new ReplenishmentSummary(
            items,
            all.Count(x => x.IsConfigured && x.SuggestedQuantity > 0
                && x.Status is ReplenishmentStatus.AtReorderLevel
                    or ReplenishmentStatus.BelowReorderLevel
                    or ReplenishmentStatus.OutOfStock),
            all.Where(x => x.IsConfigured && x.SuggestedQuantity > 0).Sum(x => x.SuggestedQuantity),
            all.Where(x => x.IsConfigured && x.SuggestedQuantity > 0).Sum(x => x.EstimatedOrderValue ?? 0),
            all.Count(x => x.IsConfigured && x.SuggestedQuantity > 0 && x.EstimatedUnitCost is null),
            unconfiguredLowStock,
            DateTime.UtcNow);
    }

    private static ReplenishmentRecommendation Build(Product product, decimal openQuantity, decimal? unitCost)
    {
        var current = product.Inventory?.QuantityOnHand ?? 0;
        var configured = product.ReplenishmentEnabled;
        var valid = product.MinimumStock >= 0 && product.ReorderQuantity >= product.MinimumStock
            && (product.Unit.SupportsDecimalQuantity()
                || (product.MinimumStock == decimal.Truncate(product.MinimumStock)
                    && product.ReorderQuantity == decimal.Truncate(product.ReorderQuantity)));

        var status = !configured ? ReplenishmentStatus.NotConfigured
            : !valid ? ReplenishmentStatus.InvalidConfiguration
            : current == 0 && product.MinimumStock > 0 ? ReplenishmentStatus.OutOfStock
            : current < product.MinimumStock ? ReplenishmentStatus.BelowReorderLevel
            : current == product.MinimumStock ? ReplenishmentStatus.AtReorderLevel
            : ReplenishmentStatus.Healthy;

        var candidate = configured && valid && current <= product.MinimumStock;
        var suggested = candidate ? Math.Max(product.ReorderQuantity - current - openQuantity, 0) : 0;
        return new ReplenishmentRecommendation(
            product.Id, product.ProductCode, product.Name, product.Unit, current,
            product.MinimumStock, product.ReorderQuantity, openQuantity, current + openQuantity,
            suggested, product.PreferredSupplierId, product.PreferredSupplier?.Name,
            unitCost, unitCost is null ? null : suggested * unitCost.Value, status, configured && valid);
    }
}
