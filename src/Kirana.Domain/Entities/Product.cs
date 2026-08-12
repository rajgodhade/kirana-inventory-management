using Kirana.Domain.Common;
using System.ComponentModel.DataAnnotations.Schema;

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

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public int? BrandId { get; set; }
    public Brand? Brand { get; set; }

    public UnitOfMeasure Unit { get; set; } = UnitOfMeasure.Piece;

    /// <summary>Optional bulk unit this product can be PURCHASED in (e.g. "Box"), converted to
    /// <see cref="Unit"/> via <see cref="PurchasePackSize"/> before touching inventory (Phase
    /// 13A). Selling/stock/pricing always stay in <see cref="Unit"/> — this never changes what
    /// <see cref="Unit"/> means. Null unless <see cref="PurchasePackSize"/> is also set.</summary>
    public UnitOfMeasure? PurchasePackUnit { get; set; }

    /// <summary>How many <see cref="Unit"/> one <see cref="PurchasePackUnit"/> equals (e.g. 12 for
    /// "1 Box = 12 Piece"). Must be positive and paired with <see cref="PurchasePackUnit"/> — see
    /// <see cref="UnitConversion.IsValidPackConfiguration"/>.</summary>
    public decimal? PurchasePackSize { get; set; }

    /// <summary>Optional shopper-friendly label override (e.g. "500g Pack") shown in place of the
    /// plain unit name on POS/receipts/reports. Cosmetic only — never used in arithmetic.</summary>
    public string? UnitDisplayText { get; set; }

    // Pricing (PRD §14)
    public decimal PurchasePrice { get; set; }
    public decimal Mrp { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? WholesalePrice { get; set; }
    public decimal? DefaultDiscountPercent { get; set; }
    public decimal? GstRatePercent { get; set; }
    public string? HsnCode { get; set; }
    public PricingType PricingType { get; set; } = PricingType.Inclusive;

    /// <summary>Compatibility bridge for older application callers. EF persists
    /// <see cref="PricingType"/>; new code should use that property directly.</summary>
    [NotMapped]
    public bool IsTaxInclusive
    {
        get => PricingType == PricingType.Inclusive;
        set => PricingType = value ? PricingType.Inclusive : PricingType.Exclusive;
    }

    public bool IsActive { get; set; } = true;
    public bool TracksBatches { get; set; }

    // Inventory thresholds (PRD §26)
    public decimal MinimumStock { get; set; }
    public decimal ReorderQuantity { get; set; }

    public Inventory? Inventory { get; set; }
    public ICollection<StockMovement> StockMovements { get; set; } = new List<StockMovement>();
    public ICollection<ProductBatch> Batches { get; set; } = new List<ProductBatch>();
    public ICollection<PromotionTarget> PromotionTargets { get; set; } = new List<PromotionTarget>();

    /// <summary>Every code that scans to this product (Phase 13B), active and retired alike.
    /// Exactly one active entry is <see cref="ProductBarcode.IsPrimary"/>.</summary>
    public ICollection<ProductBarcode> Barcodes { get; set; } = new List<ProductBarcode>();
}
