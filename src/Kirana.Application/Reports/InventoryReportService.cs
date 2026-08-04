using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Reports;

public sealed class InventoryReportService(
    IKiranaDbContext db, IInventoryService inventoryService, IPermissionEnforcer permissionEnforcer) : IInventoryReportService
{
    /// <summary>Overstock heuristic: active, non-zero reorder quantity, and on hand more than 5×
    /// the reorder quantity. <c>Product</c> carries no explicit "maximum stock" field (PRD never
    /// asked for one), so this is a documented judgement call rather than a stored threshold.</summary>
    private const decimal OverstockMultiplier = 5m;

    public async Task<IReadOnlyList<InventoryRow>> GetCurrentInventoryAsync(
        ReportFilter? filter, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);
        var canViewCost = await permissionEnforcer.HasPermissionAsync(performedByUserId, PermissionKeys.PricingViewPurchasePrice, cancellationToken);

        IQueryable<Product> products = db.Products.AsNoTracking().Where(p => p.IsActive);

        if (filter?.ProductId is { } productId)
        {
            products = products.Where(p => p.Id == productId);
        }

        if (filter?.CategoryId is { } categoryId)
        {
            products = products.Where(p => p.CategoryId == categoryId);
        }

        if (filter?.BrandId is { } brandId)
        {
            products = products.Where(p => p.BrandId == brandId);
        }

        var rows = await products
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.ProductCode,
                CategoryName = p.Category != null ? p.Category.Name : null,
                p.Unit,
                p.PurchasePrice,
                QuantityOnHand = p.Inventory != null ? p.Inventory.QuantityOnHand : 0m,
            })
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new InventoryRow
        {
            ProductId = r.Id,
            ProductName = r.Name,
            ProductCode = r.ProductCode,
            CategoryName = r.CategoryName,
            QuantityOnHand = r.QuantityOnHand,
            Unit = r.Unit.ToString(),
            StockValue = canViewCost ? r.QuantityOnHand * r.PurchasePrice : null,
        }).ToList();
    }

    public async Task<InventoryValuationSummary> GetValuationAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.PricingViewPurchasePrice, cancellationToken);

        var rows = await db.Inventories.AsNoTracking()
            .Where(i => i.Product.IsActive)
            .Select(i => new { i.QuantityOnHand, i.Product.PurchasePrice })
            .ToListAsync(cancellationToken);

        return new InventoryValuationSummary
        {
            TotalStockValue = rows.Sum(r => r.QuantityOnHand * r.PurchasePrice),
            ProductCount = rows.Count(r => r.QuantityOnHand > 0),
            TotalUnitsOnHand = rows.Sum(r => r.QuantityOnHand),
        };
    }

    public async Task<IReadOnlyList<InventoryRow>> GetLowStockAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);
        var products = await inventoryService.GetLowStockProductsAsync(cancellationToken);
        return await ToRowsAsync(products, performedByUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryRow>> GetOutOfStockAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);
        var products = await inventoryService.GetOutOfStockProductsAsync(cancellationToken);
        return await ToRowsAsync(products, performedByUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<InventoryRow>> GetOverstockAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);
        var canViewCost = await permissionEnforcer.HasPermissionAsync(performedByUserId, PermissionKeys.PricingViewPurchasePrice, cancellationToken);

        var rows = await db.Products.AsNoTracking()
            .Where(p => p.IsActive && p.ReorderQuantity > 0 && p.Inventory != null
                && p.Inventory.QuantityOnHand > p.ReorderQuantity * OverstockMultiplier)
            .Select(p => new
            {
                p.Id,
                p.Name,
                p.ProductCode,
                CategoryName = p.Category != null ? p.Category.Name : null,
                p.Unit,
                p.PurchasePrice,
                QuantityOnHand = p.Inventory!.QuantityOnHand,
            })
            .OrderByDescending(p => p.QuantityOnHand)
            .ToListAsync(cancellationToken);

        return rows.Select(r => new InventoryRow
        {
            ProductId = r.Id,
            ProductName = r.Name,
            ProductCode = r.ProductCode,
            CategoryName = r.CategoryName,
            QuantityOnHand = r.QuantityOnHand,
            Unit = r.Unit.ToString(),
            StockValue = canViewCost ? r.QuantityOnHand * r.PurchasePrice : null,
        }).ToList();
    }

    public async Task<IReadOnlyList<StockMovementRow>> GetStockMovementHistoryAsync(
        ReportDateRange range, int? productId, int? performedByUserId, int take = 200, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        IQueryable<StockMovement> movements = db.StockMovements.AsNoTracking()
            .Where(m => m.TimestampUtc >= range.StartUtc && m.TimestampUtc < range.EndUtc);

        if (productId is { } id)
        {
            movements = movements.Where(m => m.ProductId == id);
        }

        return await movements
            .OrderByDescending(m => m.TimestampUtc)
            .Take(take)
            .Select(m => new StockMovementRow
            {
                TimestampUtc = m.TimestampUtc,
                ProductName = m.Product.Name,
                ProductCode = m.Product.ProductCode,
                MovementType = m.MovementType.ToString(),
                QuantityChange = m.QuantityChange,
                NewQuantity = m.NewQuantity,
                ReferenceType = m.ReferenceType,
                ReferenceId = m.ReferenceId,
                Reason = m.Reason,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockMovementRow>> GetDamagedStockAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        return await db.StockMovements.AsNoTracking()
            .Where(m => m.MovementType == StockMovementType.Damaged && m.TimestampUtc >= range.StartUtc && m.TimestampUtc < range.EndUtc)
            .OrderByDescending(m => m.TimestampUtc)
            .Select(m => new StockMovementRow
            {
                TimestampUtc = m.TimestampUtc,
                ProductName = m.Product.Name,
                ProductCode = m.Product.ProductCode,
                MovementType = m.MovementType.ToString(),
                QuantityChange = m.QuantityChange,
                NewQuantity = m.NewQuantity,
                ReferenceType = m.ReferenceType,
                ReferenceId = m.ReferenceId,
                Reason = m.Reason,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BatchSummaryRow>> GetExpiredBatchesAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Now);
        return await db.ProductBatches.AsNoTracking()
            .Where(b => b.Quantity > 0 && b.ExpiryDate != null && b.ExpiryDate < today)
            .OrderBy(b => b.ExpiryDate)
            .Select(b => new BatchSummaryRow
            {
                ProductName = b.Product.Name,
                ProductCode = b.Product.ProductCode,
                BatchNumber = b.BatchNumber,
                Quantity = b.Quantity,
                ManufacturingDate = b.ManufacturingDate,
                ExpiryDate = b.ExpiryDate,
                IsExpired = true,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BatchSummaryRow>> GetExpiringSoonAsync(
        int withinDays, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var batches = await inventoryService.GetExpiringBatchesAsync(withinDays, cancellationToken);
        return batches.Select(b => new BatchSummaryRow
        {
            ProductName = b.Product.Name,
            ProductCode = b.Product.ProductCode,
            BatchNumber = b.BatchNumber,
            Quantity = b.Quantity,
            ManufacturingDate = b.ManufacturingDate,
            ExpiryDate = b.ExpiryDate,
            IsExpired = false,
        }).ToList();
    }

    public async Task<IReadOnlyList<BatchSummaryRow>> GetBatchSummaryAsync(int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ReportsView, cancellationToken);

        var today = DateOnly.FromDateTime(DateTime.Now);
        return await db.ProductBatches.AsNoTracking()
            .Where(b => b.Quantity > 0)
            .OrderBy(b => b.Product.Name).ThenBy(b => b.ExpiryDate)
            .Select(b => new BatchSummaryRow
            {
                ProductName = b.Product.Name,
                ProductCode = b.Product.ProductCode,
                BatchNumber = b.BatchNumber,
                Quantity = b.Quantity,
                ManufacturingDate = b.ManufacturingDate,
                ExpiryDate = b.ExpiryDate,
                IsExpired = b.ExpiryDate != null && b.ExpiryDate < today,
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<IReadOnlyList<InventoryRow>> ToRowsAsync(
        IReadOnlyList<Product> products, int? performedByUserId, CancellationToken cancellationToken)
    {
        var canViewCost = await permissionEnforcer.HasPermissionAsync(performedByUserId, PermissionKeys.PricingViewPurchasePrice, cancellationToken);

        // IInventoryService's low/out-of-stock queries only Include Inventory, not Category, so
        // Category names are resolved with a small separate lookup rather than a nav-property read
        // that would silently come back null.
        var categoryIds = products.Where(p => p.CategoryId is not null).Select(p => p.CategoryId!.Value).Distinct().ToList();
        var categoryNames = categoryIds.Count == 0
            ? []
            : await db.Categories.AsNoTracking().Where(c => categoryIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id, c => c.Name, cancellationToken);

        return products.Select(p => new InventoryRow
        {
            ProductId = p.Id,
            ProductName = p.Name,
            ProductCode = p.ProductCode,
            CategoryName = p.CategoryId is { } categoryId ? categoryNames.GetValueOrDefault(categoryId) : null,
            QuantityOnHand = p.Inventory?.QuantityOnHand ?? 0m,
            Unit = p.Unit.ToString(),
            StockValue = canViewCost ? (p.Inventory?.QuantityOnHand ?? 0m) * p.PurchasePrice : null,
        }).ToList();
    }
}
