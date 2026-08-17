namespace Kirana.Application.Reports;

public sealed class ProductSalesRow
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public decimal QuantitySold { get; init; }
    public decimal Revenue { get; init; }

    /// <summary>Null unless the caller holds <see cref="Domain.Entities.PermissionKeys.ReportsViewProfit"/>.
    /// Cost comes from <see cref="Domain.Entities.SaleItem.UnitCostSnapshot"/> — the cost recorded
    /// on each line at sale time (Phase 17A-Fix-2), never the product's current purchase price.
    /// Sale lines predating that snapshot contribute revenue but no cost, so this remains an
    /// estimate exactly where <see cref="ProfitSummary"/> is: an upper bound, not a guarantee,
    /// whenever a product's history includes such a line.</summary>
    public decimal? EstimatedProfit { get; init; }
}

public sealed class CategorySalesRow
{
    public int? CategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal QuantitySold { get; init; }
    public decimal Revenue { get; init; }
}

public sealed class BrandSalesRow
{
    public int? BrandId { get; init; }
    public string BrandName { get; init; } = string.Empty;
    public decimal QuantitySold { get; init; }
    public decimal Revenue { get; init; }
}

/// <summary>A product with zero sales inside the lookback window, and stock still sitting on the
/// shelf — money tied up in goods that are not moving (PRD §51 "Dead Stock").</summary>
public sealed class DeadStockRow
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public decimal QuantityOnHand { get; init; }
    public decimal StockValue { get; init; }
    public DateTime? LastSoldUtc { get; init; }
}
