namespace Kirana.Application.Products;

/// <summary>
/// Search by Product ID, SKU, barcode, name, category, and brand (PRD §13). Results are
/// prioritized exact-barcode &gt; exact-code/SKU &gt; partial name/category/brand match.
/// </summary>
public sealed class ProductSearchQuery
{
    public string? SearchText { get; init; }
    public int? CategoryId { get; init; }
    public int? BrandId { get; init; }
    public bool IncludeInactive { get; init; }
    public int MaxResults { get; init; } = 200;
}
