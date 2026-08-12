namespace Kirana.Domain.Entities;

/// <summary>
/// The fixed unit list from PRD §15. Units that are naturally fractional
/// (Kilogram/Gram/Litre/Millilitre) allow decimal quantities; the rest are sold whole.
/// </summary>
public enum UnitOfMeasure
{
    Piece,
    Packet,
    Box,
    Dozen,
    Kilogram,
    Gram,
    Litre,
    Millilitre,
    Bottle,
    Bag,
    Can,
    Carton,
}

public static class UnitOfMeasureExtensions
{
    private static readonly HashSet<UnitOfMeasure> DecimalCapableUnits =
    [
        UnitOfMeasure.Kilogram,
        UnitOfMeasure.Gram,
        UnitOfMeasure.Litre,
        UnitOfMeasure.Millilitre,
    ];

    public static bool SupportsDecimalQuantity(this UnitOfMeasure unit) => DecimalCapableUnits.Contains(unit);

    /// <summary>Short, shopper-friendly label for POS/receipts/reports (Phase 13A). Purely
    /// cosmetic — never affects persistence, which always uses the enum member's own name via
    /// <c>HasConversion&lt;string&gt;()</c>.</summary>
    public static string ToDisplayText(this UnitOfMeasure unit) => unit switch
    {
        UnitOfMeasure.Packet => "Pack",
        UnitOfMeasure.Kilogram => "Kg",
        UnitOfMeasure.Gram => "g",
        UnitOfMeasure.Litre => "L",
        UnitOfMeasure.Millilitre => "mL",
        _ => unit.ToString(),
    };
}
