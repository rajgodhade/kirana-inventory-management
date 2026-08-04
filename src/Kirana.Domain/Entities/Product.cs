using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Product master record (PRD §11-15). <see cref="ProductCode"/> is the stable,
/// auto-generated identifier (e.g. "PRD-000001") and must never change after creation.
/// <see cref="PurchasePrice"/> and margins derived from it are management-only data
/// (PRD §14) — application code must gate their display behind
/// <see cref="PermissionKeys.PricingViewPurchasePrice"/>, never rely on UI hiding alone.
/// </summary>
public class Product : Entity
{
    public string ProductCode { get; set; } = string.Empty;
    public string? Sku { get; set; }
    public string? Barcode { get; set; }

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Piece;

    // Pricing (PRD §14)
    public decimal PurchasePrice { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? WholesalePrice { get; set; }
    public decimal? DefaultDiscountPercent { get; set; }
    public decimal? GstRatePercent { get; set; }
    public string? HsnCode { get; set; }
    public bool IsTaxInclusive { get; set; }

    public bool IsActive { get; set; } = true;
    public bool TracksBatches { get; set; }

    // Inventory thresholds (PRD §26)
    public decimal MinimumStock { get; set; }
    public decimal ReorderQuantity { get; set; }

    public Inventory? Inventory { get; set; }
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
    public ICollection<ProductBatch> Batches { get; set; } = new List<ProductBatch>();
}
