using Kirana.Domain.Entities;

namespace Kirana.Application.Reports;

/// <summary>
/// Inventory reports (PRD §51). Reads require <see cref="PermissionKeys.ReportsView"/>.
/// <see cref="GetValuationAsync"/> additionally requires
/// <see cref="PermissionKeys.PricingViewPurchasePrice"/> — a valuation is, by definition,
/// quantity × purchase price, so it carries the same sensitivity as the purchase price itself
/// (PRD §6, §9, and <see cref="Product"/>'s own doc comment).
/// </summary>
public interface IInventoryReportService
{
    Task<IReadOnlyList<InventoryRow>> GetCurrentInventoryAsync(
        ReportFilter? filter, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<InventoryValuationSummary> GetValuationAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryRow>> GetLowStockAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<InventoryRow>> GetOutOfStockAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Products holding materially more stock than their reorder quantity calls for — see
    /// the implementation for the exact heuristic, since <c>Product</c> has no explicit maximum-stock
    /// field to compare against.</summary>
    Task<IReadOnlyList<InventoryRow>> GetOverstockAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMovementRow>> GetStockMovementHistoryAsync(
        ReportDateRange range, int? productId, int? performedByUserId, int take = 200, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockMovementRow>> GetDamagedStockAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatchSummaryRow>> GetExpiredBatchesAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatchSummaryRow>> GetExpiringSoonAsync(int withinDays, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatchSummaryRow>> GetBatchSummaryAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Completed physical stock counts and what each one adjusted (Phase 13C). Lives here
    /// rather than in a separate reporting system because "what did the stock-take find" is an
    /// inventory question, and it shares the existing <see cref="PermissionKeys.ReportsView"/> gate.</summary>
    Task<IReadOnlyList<StockCountReportRow>> GetStockCountHistoryAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);
}
