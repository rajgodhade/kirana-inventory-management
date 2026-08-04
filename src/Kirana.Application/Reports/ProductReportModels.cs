namespace Kirana.Application.Reports;

public sealed class ProductSalesRow
{
    public int ProductId { get; init; }
    public string ProductName { get; init; } = string.Empty;
    public string ProductCode { get; init; } = string.Empty;
    public decimal QuantitySold { get; init; }
    public decimal Revenue { get; init; }

    /// <summary>Null unless the caller holds <see cref="Domain.Entities.PermissionKeys.ReportsViewProfit"/> —
    /// an estimate from the product's current purchase price, same caveat as <see cref="ProfitSummary"/>.</summary>
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
