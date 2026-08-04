using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

/// <summary>
/// Input for editing an existing product. Stock is not editable here — go through
/// IInventoryService.AdjustStockAsync so a StockMovement row is always written (PRD §25, §43).
/// </summary>
public sealed class UpdateProductRequest
{
    public required string Name { get; init; }
    public string? Sku { get; init; }
    public string? Barcode { get; init; }
    public string? Description { get; init; }

    public int? CategoryId { get; init; }
    public int? BrandId { get; init; }
    public UnitOfMeasure Unit { get; init; } = UnitOfMeasure.Piece;

    public decimal PurchasePrice { get; init; }
    public decimal Mrp { get; init; }
    public decimal SellingPrice { get; init; }
    public decimal? WholesalePrice { get; init; }
    public decimal? DefaultDiscountPercent { get; init; }
    public decimal? GstRatePercent { get; init; }
    public string? HsnCode { get; init; }
    public bool IsTaxInclusive { get; init; }

    public bool TracksBatches { get; init; }
    public decimal MinimumStock { get; init; }
    public decimal ReorderQuantity { get; init; }

    public int? PerformedByUserId { get; init; }
}
