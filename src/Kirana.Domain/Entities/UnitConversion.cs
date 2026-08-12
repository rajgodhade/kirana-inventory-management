namespace Kirana.Domain.Entities;

/// <summary>
/// Deterministic, decimal-safe conversion between a product's optional purchase pack (e.g. "Box",
/// where 1 Box = 12 Piece) and its base/stock unit (Phase 13A). Purchasing/selling/inventory keep
/// operating in the base unit; this is only ever used to translate a pack-mode purchase quantity
/// into the base-unit quantity before it touches inventory.
/// </summary>
public static class UnitConversion
{
    /// <summary>Converts a quantity expressed in a pack unit into the equivalent base-unit
    /// quantity. Throws rather than silently reinterpreting an invalid conversion.</summary>
    public static decimal ToBaseQuantity(decimal packQuantity, decimal packSize, UnitOfMeasure packUnit, UnitOfMeasure baseUnit)
    {
        if (packSize <= 0)
        {
            throw new ArgumentException("Pack size must be greater than zero.", nameof(packSize));
        }

        if (packUnit == baseUnit)
        {
            throw new ArgumentException("Pack unit must be different from the product's base unit.", nameof(packUnit));
        }

        if (packQuantity <= 0)
        {
            throw new ArgumentException("Pack quantity must be greater than zero.", nameof(packQuantity));
        }

        return packQuantity * packSize;
    }

    /// <summary>Non-throwing check used by validation/import paths: a product's optional pack
    /// configuration is valid iff both pack fields are absent (no pack configured) or both are
    /// present with a positive size and a pack unit different from the base unit.</summary>
    public static bool IsValidPackConfiguration(decimal? packSize, UnitOfMeasure? packUnit, UnitOfMeasure baseUnit)
    {
        if (packSize is null && packUnit is null)
        {
            return true;
        }

        if (packSize is null || packUnit is null)
        {
            return false;
        }

        return packSize.Value > 0 && packUnit.Value != baseUnit;
    }
}
