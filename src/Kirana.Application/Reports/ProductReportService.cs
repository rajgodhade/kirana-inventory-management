using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Reports;

public sealed class ProductReportService(IKiranaDbContext db, IPermissionEnforcer permissionEnforcer) : IProductReportService
{
    public async Task<IReadOnlyList<ProductSalesRow>> GetMostSellingAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default)
    {
        var rows = await GetSoldProductRowsAsync(range, filter: null, performedByUserId, cancellationToken);
        return rows.OrderByDescending(r => r.QuantitySold).Take(take).ToList();
    }

    public async Task<IReadOnlyList<ProductSalesRow>> GetLeastSellingAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default)
    {
        // Only products that sold at least once — zero-sale products belong to Dead Stock instead,
        // where "still has stock and never moved" is the point rather than "moved the least."
        var rows = await GetSoldProductRowsAsync(range, filter: null, performedByUserId, cancellationToken);
        return rows.Where(r => r.QuantitySold > 0).OrderBy(r => r.QuantitySold).Take(take).ToList();
    }

    public async Task<IReadOnlyList<ProductSalesRow>> GetHighestRevenueAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default)
    {
        var rows = await GetSoldProductRowsAsync(range, filter: null, performedByUserId, cancellationToken);
        return rows.OrderByDescending(r => r.Revenue).Take(take).ToList();
    }

    public async Task<IReadOnlyList<ProductSalesRow>> GetHighestProfitProductsAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsViewProfit, cancellationToken);

        var rows = await GetSoldProductRowsAsync(range, filter: null, performedByUserId, cancellationToken);
        return rows.OrderByDescending(r => r.EstimatedProfit ?? 0m).Take(take).ToList();
    }

    public async Task<IReadOnlyList<ProductSalesRow>> GetSlowMovingAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var sold = await BuildSoldAggregatesAsync(range, filter: null, cancellationToken);
        var soldByProduct = sold.ToDictionary(s => s.ProductId);

        var active = await db.Products.AsNoTracking()
            .Where(p => p.IsActive)
            .Select(p => new { p.Id, p.Name, p.ProductCode, StockOnHand = p.Inventory != null ? p.Inventory.QuantityOnHand : 0m })
            .ToListAsync(cancellationToken);

        var canViewProfit = await permissionEnforcer.HasPermissionAsync(performedByUserId, PermissionKeys.ReportsViewProfit, cancellationToken);

        return active
            .Select(p =>
            {
                soldByProduct.TryGetValue(p.Id, out var agg);
                return new ProductSalesRow
                {
                    ProductId = p.Id,
                    ProductName = p.Name,
                    ProductCode = p.ProductCode,
                    QuantitySold = agg?.Quantity ?? 0m,
                    Revenue = agg?.Revenue ?? 0m,
                    EstimatedProfit = canViewProfit ? (agg?.Revenue ?? 0m) - (agg?.Cost ?? 0m) : null,
                };
            })
            .OrderBy(r => r.QuantitySold)
            .Take(take)
            .ToList();
    }

    public async Task<IReadOnlyList<DeadStockRow>> GetDeadStockAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var soldProductIds = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc)
            .Select(i => i.ProductId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var candidates = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.Inventory != null && p.Inventory.QuantityOnHand > 0 && !soldProductIds.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.ProductCode,
                p.PurchasePrice,
                StockOnHand = p.Inventory!.QuantityOnHand,
            })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 0)
        {
            return [];
        }

        var candidateIds = candidates.Select(c => c.Id).ToList();
        var lastSoldByProduct = await db.SaleItems.AsNoTracking()
            .Where(i => candidateIds.Contains(i.ProductId))
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, LastSoldUtc = g.Max(x => x.Sale.SaleDateUtc) })
            .ToDictionaryAsync(x => x.ProductId, x => x.LastSoldUtc, cancellationToken);

        return candidates
            .Select(c => new DeadStockRow
            {
                ProductId = c.Id,
                ProductName = c.Name,
                ProductCode = c.ProductCode,
                QuantityOnHand = c.StockOnHand,
                StockValue = c.StockOnHand * c.PurchasePrice,
                LastSoldUtc = lastSoldByProduct.GetValueOrDefault(c.Id),
            })
            .OrderByDescending(r => r.StockValue)
            .ToList();
    }

    public async Task<IReadOnlyList<ProductSalesRow>> GetProductWiseSalesAsync(
        ReportDateRange range, ReportFilter? filter, int? performedByUserId, CancellationToken cancellationToken = default) =>
        await GetSoldProductRowsAsync(range, filter, performedByUserId, cancellationToken);

    public async Task<IReadOnlyList<CategorySalesRow>> GetCategoryWiseSalesAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var raw = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc)
            .GroupBy(i => new { i.Product.CategoryId, CategoryName = i.Product.Category != null ? i.Product.Category.Name : "Uncategorized" })
            .Select(g => new CategorySalesRow
            {
                CategoryId = g.Key.CategoryId,
                CategoryName = g.Key.CategoryName,
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal),
            })
            .OrderByDescending(r => r.Revenue)
            .ToListAsync(cancellationToken);

        return raw;
    }

    public async Task<IReadOnlyList<BrandSalesRow>> GetBrandWiseSalesAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var raw = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc)
            .GroupBy(i => new { i.Product.BrandId, BrandName = i.Product.Brand != null ? i.Product.Brand.Name : "Unbranded" })
            .Select(g => new BrandSalesRow
            {
                BrandId = g.Key.BrandId,
                BrandName = g.Key.BrandName,
                QuantitySold = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal),
            })
            .OrderByDescending(r => r.Revenue)
            .ToListAsync(cancellationToken);

        return raw;
    }

    // ------------------------------------------------------------ shared aggregation

    private async Task<IReadOnlyList<ProductSalesRow>> GetSoldProductRowsAsync(
        ReportDateRange range, ReportFilter? filter, int? performedByUserId, CancellationToken cancellationToken)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var canViewProfit = await permissionEnforcer.HasPermissionAsync(performedByUserId, PermissionKeys.ReportsViewProfit, cancellationToken);
        var aggregates = await BuildSoldAggregatesAsync(range, filter, cancellationToken);

        return aggregates
            .Select(a => new ProductSalesRow
            {
                ProductId = a.ProductId,
                ProductName = a.Name,
                ProductCode = a.ProductCode,
                QuantitySold = a.Quantity,
                Revenue = a.Revenue,
                EstimatedProfit = canViewProfit ? a.Revenue - a.Cost : null,
            })
            .ToList();
    }

    private sealed record SoldAggregate(int ProductId, string Name, string ProductCode, decimal Quantity, decimal Revenue, decimal Cost);

    private async Task<List<SoldAggregate>> BuildSoldAggregatesAsync(ReportDateRange range, ReportFilter? filter, CancellationToken cancellationToken)
    {
        IQueryable<SaleItem> items = db.SaleItems.AsNoTracking()
            .Where(i => i.Sale.Status == SaleStatus.Completed && i.Sale.SaleDateUtc >= range.StartUtc && i.Sale.SaleDateUtc < range.EndUtc);

        if (filter?.ProductId is { } productId)
        {
            items = items.Where(i => i.ProductId == productId);
        }

        if (filter?.CategoryId is { } categoryId)
        {
            items = items.Where(i => i.Product.CategoryId == categoryId);
        }

        if (filter?.BrandId is { } brandId)
        {
            items = items.Where(i => i.Product.BrandId == brandId);
        }

        // Historical cost basis (Phase 17A-Fix-2), matching ProfitReportService and the Dashboard
        // trend: cost comes from the SNAPSHOT captured on each line at sale time, never the
        // product's current purchase price. This grouping used to include i.Product.PurchasePrice
        // in the key and multiply it by total quantity once — current master data, read at report
        // time — so repricing a product silently changed "Estimated Profit" for sales made before
        // the reprice. UnitCostSnapshot is NOT constant per product the way PurchasePrice was
        // (different lines can carry different, or no, snapshot), so cost is summed per line
        // inside the group rather than multiplied once outside it.
        //
        // Lines with no snapshot are EXCLUDED from cost, not zeroed — the same rule as everywhere
        // else this policy applies. QuantitySold and Revenue are unaffected: only the cost side of
        // an unknown-cost line is missing, exactly as it already may be for other reasons (a
        // product filter, a date boundary). ProductSalesRow has no per-row disclosure field, and
        // its "Est. Profit" label already carries that hedge, so none was added here.
        var grouped = await items
            .GroupBy(i => new { i.ProductId, i.Product.Name, i.Product.ProductCode })
            .Select(g => new
            {
                g.Key.ProductId,
                g.Key.Name,
                g.Key.ProductCode,
                Quantity = g.Sum(x => x.Quantity),
                Revenue = g.Sum(x => x.LineTotal),
                Cost = g.Where(x => x.UnitCostSnapshot != null).Sum(x => x.Quantity * x.UnitCostSnapshot!.Value),
            })
            .ToListAsync(cancellationToken);

        return grouped
            .Select(g => new SoldAggregate(g.ProductId, g.Name, g.ProductCode, g.Quantity, g.Revenue, g.Cost))
            .ToList();
    }
}
