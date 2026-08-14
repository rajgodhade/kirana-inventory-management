using Kirana.Domain.Entities;

namespace Kirana.Application.Products;

/// <summary>
/// Everything the resolver needs to know to answer "what does this product cost?".
///
/// <para>Deliberately minimal: today the only input is which <see cref="PriceLevel"/> applies.
/// It exists as a type rather than a bare parameter so later phases can add what they genuinely
/// need — a customer, a quantity, a date — without changing every call site or the resolver's
/// signature. Nothing is added speculatively; a field nobody reads is a field that will be
/// mis-set.</para>
/// </summary>
public sealed record PricingContext(PriceLevel PriceLevel)
{
    /// <summary>The shelf price every sale uses today.</summary>
    public static readonly PricingContext Retail = new(PriceLevel.Retail);

    /// <summary>Only meaningful for products that actually configure the level — asking for it does
    /// not mean a product has it.</summary>
    public static readonly PricingContext Wholesale = new(PriceLevel.Wholesale);
}
