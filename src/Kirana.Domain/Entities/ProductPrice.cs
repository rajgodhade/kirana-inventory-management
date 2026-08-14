using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>
/// One selling price of a product at one <see cref="PriceLevel"/> (Phase 15A).
///
/// <para><b>This is the authoritative store for selling prices.</b> <c>Product.SellingPrice</c> and
/// <c>Product.WholesalePrice</c> remain as a synchronised projection, written in the same
/// transaction, purely so the existing POS/report/export readers keep working untouched — every
/// write still goes through <c>IProductPricingService</c>, so there is one write path, not two
/// competing ones. Phase 15B retires the projection once POS resolves prices through the service.</para>
///
/// <para><b>Not a price history.</b> A row is updated in place and the change is recorded in the
/// audit trail; <see cref="IsActive"/> exists so a level can be withdrawn (e.g. a product stops
/// being sold wholesale) without deleting the row it had. Historical transactions are protected by
/// their own snapshots on <see cref="SaleItem"/> and are never recomputed from here.</para>
/// </summary>
public class ProductPrice : Entity
{
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public PriceLevel Level { get; set; }

    /// <summary>The price in store currency. Always >= 0; zero is permitted because the existing
    /// catalogue already allows zero-priced products.</summary>
    public decimal Price { get; set; }

    /// <summary>False when this level no longer applies. At most one ACTIVE row may exist per
    /// product per level, enforced by a filtered unique index rather than by service checks alone.</summary>
    public bool IsActive { get; set; } = true;
}
