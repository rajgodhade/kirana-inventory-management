using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

/// <summary>One requested purchase line. GST rate/tax-inclusive flag always come from the
/// product's current master data (like billing) — only <see cref="UnitPrice"/> (the negotiated
/// purchase price) and discount are supplied fresh per purchase.</summary>
public sealed class PurchaseLineInput
{
    public required int ProductId { get; init; }

    /// <summary>Always the base-unit quantity (Phase 13A) — the single authoritative source of
    /// truth for how much stock this line adds, whether entered directly or converted from a
    /// pack. When <see cref="PurchasedPackUnit"/> is also set, the server verifies this already
    /// equals <c>PurchasedPackQuantity * Product.PurchasePackSize</c> rather than deriving it.</summary>
    public required decimal Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public decimal DiscountPercent { get; init; }
    public PricingType? PricingType { get; init; }

    /// <summary>Optional audit metadata (Phase 13A): the pack this line was entered in (e.g.
    /// "10 Box"). Supplementary only — <see cref="Quantity"/> above remains authoritative and
    /// must already reflect the converted base-unit amount.</summary>
    public UnitOfMeasure? PurchasedPackUnit { get; init; }
    public decimal? PurchasedPackQuantity { get; init; }

    /// <summary>Batch/expiry data captured at purchase time (PRD §27) — only meaningful when the
    /// product tracks batches; ignored otherwise.</summary>
    public string? BatchNumber { get; init; }
    public DateOnly? ManufacturingDate { get; init; }
    public DateOnly? ExpiryDate { get; init; }
}
