using Kirana.Domain.Entities;

namespace Kirana.Tests.Products;

public class UnitConversionTests
{
    [Fact]
    public void ToBaseQuantity_MultipliesPackQuantityByPackSize()
    {
        var result = UnitConversion.ToBaseQuantity(packQuantity: 10, packSize: 12, UnitOfMeasure.Box, UnitOfMeasure.Piece);

        Assert.Equal(120m, result);
    }

    [Fact]
    public void ToBaseQuantity_ConvertsBoxToPiece()
    {
        var result = UnitConversion.ToBaseQuantity(1, 12, UnitOfMeasure.Box, UnitOfMeasure.Piece);

        Assert.Equal(12m, result);
    }

    [Fact]
    public void ToBaseQuantity_ChainsCartonToBoxToPiece()
    {
        // 1 Carton = 24 Box, 1 Box = 12 Piece => 1 Carton = 288 Piece.
        var boxesPerCarton = UnitConversion.ToBaseQuantity(1, 24, UnitOfMeasure.Carton, UnitOfMeasure.Box);
        var piecesPerCarton = UnitConversion.ToBaseQuantity(boxesPerCarton, 12, UnitOfMeasure.Box, UnitOfMeasure.Piece);

        Assert.Equal(288m, piecesPerCarton);
    }

    [Fact]
    public void ToBaseQuantity_ConvertsKilogramToGram()
    {
        var result = UnitConversion.ToBaseQuantity(1, 1000, UnitOfMeasure.Kilogram, UnitOfMeasure.Gram);

        Assert.Equal(1000m, result);
    }

    [Fact]
    public void ToBaseQuantity_ConvertsLitreToMillilitre()
    {
        var result = UnitConversion.ToBaseQuantity(1, 1000, UnitOfMeasure.Litre, UnitOfMeasure.Millilitre);

        Assert.Equal(1000m, result);
    }

    [Fact]
    public void ToBaseQuantity_PreservesDecimalQuantities()
    {
        var result = UnitConversion.ToBaseQuantity(2.5m, 1000, UnitOfMeasure.Kilogram, UnitOfMeasure.Gram);

        Assert.Equal(2500m, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void ToBaseQuantity_Throws_WhenPackSizeIsZeroOrNegative(decimal packSize)
    {
        Assert.Throws<ArgumentException>(() =>
            UnitConversion.ToBaseQuantity(10, packSize, UnitOfMeasure.Box, UnitOfMeasure.Piece));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ToBaseQuantity_Throws_WhenPackQuantityIsZeroOrNegative(decimal packQuantity)
    {
        Assert.Throws<ArgumentException>(() =>
            UnitConversion.ToBaseQuantity(packQuantity, 12, UnitOfMeasure.Box, UnitOfMeasure.Piece));
    }

    [Fact]
    public void ToBaseQuantity_Throws_WhenPackUnitEqualsBaseUnit()
    {
        // A self-conversion (e.g. "1 Piece = 1 Piece") is nonsensical and must be rejected —
        // this is the "prevent invalid self/cyclic conversions" rule from the spec.
        Assert.Throws<ArgumentException>(() =>
            UnitConversion.ToBaseQuantity(10, 12, UnitOfMeasure.Piece, UnitOfMeasure.Piece));
    }

    [Fact]
    public void IsValidPackConfiguration_True_WhenBothNull()
    {
        Assert.True(UnitConversion.IsValidPackConfiguration(null, null, UnitOfMeasure.Piece));
    }

    [Fact]
    public void IsValidPackConfiguration_True_WhenBothSetAndValid()
    {
        Assert.True(UnitConversion.IsValidPackConfiguration(12, UnitOfMeasure.Box, UnitOfMeasure.Piece));
    }

    [Fact]
    public void IsValidPackConfiguration_False_WhenOnlySizeIsSet()
    {
        Assert.False(UnitConversion.IsValidPackConfiguration(12, null, UnitOfMeasure.Piece));
    }

    [Fact]
    public void IsValidPackConfiguration_False_WhenOnlyUnitIsSet()
    {
        Assert.False(UnitConversion.IsValidPackConfiguration(null, UnitOfMeasure.Box, UnitOfMeasure.Piece));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void IsValidPackConfiguration_False_WhenPackSizeNonPositive(decimal packSize)
    {
        Assert.False(UnitConversion.IsValidPackConfiguration(packSize, UnitOfMeasure.Box, UnitOfMeasure.Piece));
    }

    [Fact]
    public void IsValidPackConfiguration_False_WhenPackUnitEqualsBaseUnit()
    {
        Assert.False(UnitConversion.IsValidPackConfiguration(12, UnitOfMeasure.Piece, UnitOfMeasure.Piece));
    }
}
