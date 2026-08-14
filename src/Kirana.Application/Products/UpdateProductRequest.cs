using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

/// <summary>
/// Input for editing an existing product. Stock is not editable here — go through
/// IInventoryService.AdjustStockAsync so a StockMovement row is always written (PRD §25, §43).
/// </summary>
public sealed class UpdateProductRequest
{
    private PricingType _pricingType = PricingType.Inclusive;
    public required string Name { get; init; }
    public string? Sku { get; init; }

    // No Barcode here by design (Phase 13B): a product owns many barcodes, and each add/retire/
    // set-primary goes through IBarcodeService so the one-primary and global-uniqueness invariants
    // live in exactly one place rather than being re-implemented inside UpdateAsync.
    public string? Description { get; init; }

    public int? CategoryId { get; init; }
    public int? BrandId { get; init; }
    public UnitOfMeasure Unit { get; init; } = UnitOfMeasure.Piece;

    /// <summary>Optional purchase pack (Phase 13A) — both null or both set, see
    /// <see cref="Kirana.Domain.Entities.UnitConversion.IsValidPackConfiguration"/>.</summary>
    public UnitOfMeasure? PurchasePackUnit { get; init; }
    public decimal? PurchasePackSize { get; init; }
    public string? UnitDisplayText { get; init; }

    public decimal PurchasePrice { get; init; }
    public decimal Mrp { get; init; }
    public decimal SellingPrice { get; init; }
    public decimal? WholesalePrice { get; init; }
    public decimal? DefaultDiscountPercent { get; init; }
    public decimal? GstRatePercent { get; init; }
    public string? HsnCode { get; init; }
    public PricingType PricingType { get => _pricingType; init => _pricingType = value; }
    public bool IsTaxInclusive { get => _pricingType == PricingType.Inclusive; init => _pricingType = value ? PricingType.Inclusive : PricingType.Exclusive; }

    public bool TracksBatches { get; init; }
    public decimal MinimumStock { get; init; }
    public decimal ReorderQuantity { get; init; }
    /// <summary>
    /// Opts this update into changing the Phase 14D replenishment configuration. Older update
    /// paths (for example product import) leave this false so they cannot silently disable or
    /// clear an operator's existing replenishment settings.
    /// </summary>
    public bool UpdateReplenishmentConfiguration { get; init; }
    public bool ReplenishmentEnabled { get; init; }
    public int? PreferredSupplierId { get; init; }

    public int? PerformedByUserId { get; init; }
}
