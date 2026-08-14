using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Inventories;

public sealed class InventoryService(IKiranaDbContext db, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer) : IInventoryService
{
    public async Task<decimal> GetStockAsync(int productId, CancellationToken cancellationToken = default)
    {
        var inventory = await db.Inventories.FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
        return inventory?.QuantityOnHand ?? 0;
    }

    public async Task<StockMovement> AdjustStockAsync(
        int productId,
        decimal quantityChange,
        StockMovementType movementType,
        string? reason,
        int? performedByUserId,
        string? referenceType = null,
        string? referenceId = null,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        if (quantityChange == 0)
        {
            throw new ArgumentException("Quantity change cannot be zero.", nameof(quantityChange));
        }

        var isIncrease = movementType.IsIncrease();
        if (isIncrease && quantityChange < 0)
        {
            throw new ArgumentException($"{movementType} must have a positive quantity change.", nameof(quantityChange));
        }

        if (!isIncrease && quantityChange > 0)
        {
            throw new ArgumentException($"{movementType} must have a negative quantity change.", nameof(quantityChange));
        }

        var productExists = await db.Products.AnyAsync(p => p.Id == productId, cancellationToken);
        if (!productExists)
        {
            throw new InvalidOperationException("Product not found.");
        }

        var inventory = await db.Inventories.FirstOrDefaultAsync(i => i.ProductId == productId, cancellationToken);
        if (inventory is null)
        {
            inventory = new Inventory { ProductId = productId, QuantityOnHand = 0 };
            db.Inventories.Add(inventory);
        }

        var previousQuantity = inventory.QuantityOnHand;
        var newQuantity = previousQuantity + quantityChange;
        inventory.QuantityOnHand = newQuantity;
        inventory.UpdatedAtUtc = DateTime.UtcNow;

        var movement = new StockMovement
        {
            ProductId = productId,
            MovementType = movementType,
            QuantityChange = quantityChange,
            PreviousQuantity = previousQuantity,
            NewQuantity = newQuantity,
            UserId = performedByUserId,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            Reason = reason,
        };
        db.StockMovements.Add(movement);

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "StockAdjustment", "Inventory", productId.ToString(),
            previousValue: previousQuantity.ToString("0.###"),
            newValue: newQuantity.ToString("0.###"),
            reason: reason,
            cancellationToken: cancellationToken);

        return movement;
    }

    public async Task<IReadOnlyList<StockMovement>> GetMovementHistoryAsync(int productId, int take = 50, CancellationToken cancellationToken = default) =>
        await db.StockMovements
            .Where(m => m.ProductId == productId)
            .OrderByDescending(m => m.TimestampUtc)
            .Take(take)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default) =>
        await db.Products
            .Include(p => p.Inventory)
            .Where(p => p.IsActive && p.MinimumStock > 0
                && p.Inventory != null
                && p.Inventory.QuantityOnHand > 0
                && p.Inventory.QuantityOnHand <= p.MinimumStock)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Product>> GetOutOfStockProductsAsync(CancellationToken cancellationToken = default) =>
        await db.Products
            .Include(p => p.Inventory)
            .Where(p => p.IsActive && (p.Inventory == null || p.Inventory.QuantityOnHand <= 0))
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

    public async Task<ProductBatch> AddBatchAsync(
        int productId,
        string batchNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate,
        decimal quantity,
        decimal? purchasePrice,
        decimal? sellingPrice,
        int? performedByUserId,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        if (string.IsNullOrWhiteSpace(batchNumber))
        {
            throw new ArgumentException("Batch number is required.", nameof(batchNumber));
        }

        if (quantity < 0)
        {
            throw new ArgumentException("Batch quantity cannot be negative.", nameof(quantity));
        }

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, cancellationToken)
            ?? throw new InvalidOperationException("Product not found.");

        var batch = new ProductBatch
        {
            ProductId = product.Id,
            BatchNumber = batchNumber.Trim(),
            ManufacturingDate = manufacturingDate,
            ExpiryDate = expiryDate,
            Quantity = quantity,
            PurchasePrice = purchasePrice,
            SellingPrice = sellingPrice,
        };
        db.ProductBatches.Add(batch);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "BatchAdded", nameof(ProductBatch), batch.Id.ToString(),
            newValue: $"{product.ProductCode} batch {batch.BatchNumber}", cancellationToken: cancellationToken);

        return batch;
    }

    public async Task<IReadOnlyList<ProductBatch>> GetBatchesAsync(int productId, CancellationToken cancellationToken = default) =>
        await db.ProductBatches
            .Where(b => b.ProductId == productId)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(cancellationToken);

    public async Task UpdateBatchExpiryAsync(
        int batchId,
        DateOnly? expiryDate,
        int? performedByUserId,
        CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.InventoryManage, cancellationToken);

        var batch = await db.ProductBatches
            .Include(b => b.Product)
            .FirstOrDefaultAsync(b => b.Id == batchId, cancellationToken)
            ?? throw new InvalidOperationException("Product batch not found.");

        var previousExpiry = batch.ExpiryDate;
        batch.ExpiryDate = expiryDate;
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "BatchExpiryUpdated", nameof(ProductBatch), batch.Id.ToString(),
            previousValue: previousExpiry?.ToString("yyyy-MM-dd"),
            newValue: expiryDate?.ToString("yyyy-MM-dd"), cancellationToken: cancellationToken);
    }

    public async Task<IReadOnlyList<ProductBatch>> GetExpiringBatchesAsync(int withinDays, CancellationToken cancellationToken = default)
    {
        var threshold = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(withinDays));
        return await db.ProductBatches
            .Include(b => b.Product)
            .Where(b => b.ExpiryDate != null && b.ExpiryDate <= threshold && b.Quantity > 0)
            .OrderBy(b => b.ExpiryDate)
            .ToListAsync(cancellationToken);
    }
}
