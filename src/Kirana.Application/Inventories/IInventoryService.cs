using Kirana.Domain.Entities;

namespace Kirana.Application.Inventories;

/// <summary>
/// Real-time stock and the movement ledger (PRD §24-27). Every quantity change must go
/// through <see cref="AdjustStockAsync"/> so a <see cref="StockMovement"/> row is written
/// atomically alongside the <see cref="Domain.Entities.Inventory"/> update (PRD §43).
/// </summary>
public interface IInventoryService
{
    Task<decimal> GetStockAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Applies a signed quantity change of the given movement type. The sign of
    /// <paramref name="quantityChange"/> must match whether <paramref name="movementType"/>
    /// is an increase or decrease (see <see cref="StockMovementTypeExtensions.IsIncrease"/>).
    /// </summary>
    Task<StockMovement> AdjustStockAsync(
        int productId,
        decimal quantityChange,
        StockMovementType movementType,
        string? reason,
        int? performedByUserId,
        string? referenceType = null,
        string? referenceId = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMovement>> GetMovementHistoryAsync(int productId, int take = 50, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> GetLowStockProductsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Product>> GetOutOfStockProductsAsync(CancellationToken cancellationToken = default);

    Task<ProductBatch> AddBatchAsync(
        int productId,
        string batchNumber,
        DateOnly? manufacturingDate,
        DateOnly? expiryDate,
        decimal quantity,
        decimal? purchasePrice,
        decimal? sellingPrice,
        int? performedByUserId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductBatch>> GetBatchesAsync(int productId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductBatch>> GetExpiringBatchesAsync(int withinDays, CancellationToken cancellationToken = default);
}
