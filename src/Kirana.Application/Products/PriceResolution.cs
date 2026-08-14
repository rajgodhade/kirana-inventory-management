using System.Diagnostics.CodeAnalysis;
using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

/// <summary>Where a resolved price came from. Only one origin exists in Phase 15B-1 — a price an
/// operator configured on the product. Promotions, customer agreements and quantity breaks are
/// later phases and will add members here rather than a second result type.</summary>
public enum PriceSource
{
    /// <summary>An active <see cref="ProductPrice"/> row for the requested level.</summary>
    ConfiguredPrice,
}

/// <summary>Why no price could be resolved. Distinct reasons because a till showing "this item has
/// no wholesale price" is a different message from "this item is discontinued".</summary>
public enum PriceUnavailableReason
{
    /// <summary>The product carries no active price at the requested level.</summary>
    LevelNotConfigured,

    /// <summary>The product exists but is inactive, so it has no current selling price at all.</summary>
    ProductInactive,
}

/// <summary>
/// The answer to a pricing question: either a price, or a stated reason there isn't one.
///
/// <para>Modelled as an outcome rather than a nullable decimal on purpose. "No wholesale price" is a
/// legitimate, expected answer — not an error and not zero — and a caller that has to distinguish it
/// from a real price of 0 cannot do so with a null alone. Callers check
/// <see cref="IsResolved"/> before reading <see cref="UnitPrice"/>; the compiler enforces it.</para>
/// </summary>
public sealed record PriceResolution
{
    private PriceResolution(
        int productId, PriceLevel level, decimal? unitPrice,
        PriceSource? source, PriceUnavailableReason? unavailableReason)
    {
        ProductId = productId;
        Level = level;
        UnitPrice = unitPrice;
        Source = source;
        UnavailableReason = unavailableReason;
    }

    public int ProductId { get; }

    /// <summary>The level that was ASKED for, echoed back even when unavailable — so a caller
    /// logging a failure knows which level it wanted.</summary>
    public PriceLevel Level { get; }

    /// <summary>The price, in the same decimal money semantics as the stored value. Null only when
    /// <see cref="IsResolved"/> is false.</summary>
    public decimal? UnitPrice { get; }

    public PriceSource? Source { get; }

    public PriceUnavailableReason? UnavailableReason { get; }

    [MemberNotNullWhen(true, nameof(UnitPrice))]
    [MemberNotNullWhen(true, nameof(Source))]
    public bool IsResolved => UnavailableReason is null;

    public static PriceResolution Resolved(int productId, PriceLevel level, decimal unitPrice) =>
        new(productId, level, unitPrice, PriceSource.ConfiguredPrice, null);

    public static PriceResolution Unavailable(int productId, PriceLevel level, PriceUnavailableReason reason) =>
        new(productId, level, null, null, reason);
}
