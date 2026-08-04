namespace Kirana.App.ViewModels;

/// <summary>Flattened, permission-aware row for the products list (PRD §12-14, §26).</summary>
public sealed class ProductRowViewModel
{
    public required int Id { get; init; }
    public required string ProductCode { get; init; }
    public required string Name { get; init; }
    public string? Sku { get; init; }
    public string? Barcode { get; init; }
    public string CategoryName { get; init; } = "";
    public string BrandName { get; init; } = "";
    public required string Unit { get; init; }

    public decimal SellingPrice { get; init; }
    public decimal? Mrp { get; init; }
    public decimal? PurchasePrice { get; init; }
    public bool ShowPurchasePrice { get; init; }

    public decimal Stock { get; init; }
    public bool IsActive { get; init; }
    public bool TracksBatches { get; init; }
    public string StockStatus { get; init; } = "";
}
