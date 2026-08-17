namespace Kirana.Domain.Entities;

/// <summary>
/// A named selling price a product can carry (Phase 15A). Strongly typed rather than magic strings
/// so a typo is a compile error, and so adding a level later is a single member here instead of a
/// column, a migration and a UI binding.
///
/// <para>Persisted by NAME (see ProductPriceConfiguration), so reordering these members can never
/// silently reinterpret historical rows.</para>
///
/// <para>Deliberately NOT added in 15A: Customer, VIP, Distributor. The model is shaped to accept
/// them, but nothing resolves them yet — that is Phase 15B.</para>
/// </summary>
public enum PriceLevel
{
    /// <summary>The everyday shelf price. Every product has one, and it is what POS charges today —
    /// this is the level the pre-15A <c>Product.SellingPrice</c> column always meant.</summary>
    Retail,

    /// <summary>Optional bulk/trade price. A product may have none, which is not the same as zero:
    /// "no wholesale price configured" means wholesale simply does not apply.</summary>
    Wholesale,
}

public static class PriceLevelExtensions
{
    /// <summary>Every product must always have this level; the others are optional.</summary>
    public static bool IsRequired(this PriceLevel level) => level == PriceLevel.Retail;

    public static string ToDisplayText(this PriceLevel level) => level switch
    {
        PriceLevel.Retail => "Retail",
        PriceLevel.Wholesale => "Wholesale",
        _ => level.ToString(),
    };
}
