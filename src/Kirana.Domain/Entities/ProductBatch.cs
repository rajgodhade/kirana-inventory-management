using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// Optional per-product batch/expiry tracking (PRD §27). Only meaningful when
/// <see cref="Product.TracksBatches"/> is true; batch quantities are supplementary detail
/// and do not replace <see cref="Inventory.QuantityOnHand"/> as the authoritative stock figure.
/// </summary>
public class ProductBatch : Entity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public string BatchNumber { get; set; } = string.Empty;
    public DateOnly? ManufacturingDate { get; set; }
    public DateOnly? ExpiryDate { get; set; }
    public decimal Quantity { get; set; }
    public decimal? PurchasePrice { get; set; }
    public decimal? SellingPrice { get; set; }
}
