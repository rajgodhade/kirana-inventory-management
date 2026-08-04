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
}
