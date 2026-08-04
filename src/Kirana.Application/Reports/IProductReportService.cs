namespace Kirana.Application.Reports;

/// <summary>Product, category and brand performance reports (PRD §51). Reads require
/// <see cref="Domain.Entities.PermissionKeys.ReportsView"/>; <see cref="GetHighestProfitProductsAsync"/>
/// additionally requires <see cref="Domain.Entities.PermissionKeys.ReportsViewProfit"/>.</summary>
public interface IProductReportService
{
    Task<IReadOnlyList<ProductSalesRow>> GetMostSellingAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSalesRow>> GetLeastSellingAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSalesRow>> GetHighestRevenueAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSalesRow>> GetHighestProfitProductsAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default);

    /// <summary>Active products that sold the least (including zero) within the range, largest
    /// stock-on-hand first — the candidates worth marking down or discontinuing.</summary>
    Task<IReadOnlyList<ProductSalesRow>> GetSlowMovingAsync(
        ReportDateRange range, int? performedByUserId, int take = 20, CancellationToken cancellationToken = default);

    /// <summary>Active products with stock on hand and zero sales anywhere inside the range.</summary>
    Task<IReadOnlyList<DeadStockRow>> GetDeadStockAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProductSalesRow>> GetProductWiseSalesAsync(
        ReportDateRange range, ReportFilter? filter, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategorySalesRow>> GetCategoryWiseSalesAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BrandSalesRow>> GetBrandWiseSalesAsync(
        ReportDateRange range, int? performedByUserId, CancellationToken cancellationToken = default);
}
