using Kirana.Application.Purchasing;

namespace Kirana.Tests.Purchasing;

public class PurchasePricingCalculatorTests
{
    private static PurchaseLine Line(
        int productId = 1, decimal quantity = 1, decimal unitPrice = 100,
        bool isTaxInclusive = false, decimal gstRatePercent = 0, decimal discountPercent = 0) => new()
    {
        ProductId = productId,
        Quantity = quantity,
        UnitPrice = unitPrice,
        IsTaxInclusive = isTaxInclusive,
        GstRatePercent = gstRatePercent,
        DiscountPercent = discountPercent,
    };

    [Fact]
    public void Calculate_NoDiscountNoGst_TotalsMatchGrossAmount()
    {
        var totals = PurchasePricingCalculator.Calculate([Line(quantity: 3, unitPrice: 50)]);

        Assert.Equal(150m, totals.SubTotal);
        Assert.Equal(0m, totals.DiscountTotal);
        Assert.Equal(0m, totals.TaxTotal);
        Assert.Equal(150m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_TaxExclusive_AddsGstOnTopOfPrice()
    {
        var totals = PurchasePricingCalculator.Calculate([Line(quantity: 1, unitPrice: 100, gstRatePercent: 18)]);

        Assert.Equal(100m, totals.TaxableTotal);
        Assert.Equal(18m, totals.TaxTotal);
        Assert.Equal(118m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_TaxInclusive_BackCalculatesGstFromPrice()
    {
        var totals = PurchasePricingCalculator.Calculate([Line(quantity: 1, unitPrice: 118, isTaxInclusive: true, gstRatePercent: 18)]);

        Assert.Equal(100m, totals.TaxableTotal);
        Assert.Equal(18m, totals.TaxTotal);
        Assert.Equal(118m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_AppliesItemDiscount_BeforeGst()
    {
        var totals = PurchasePricingCalculator.Calculate([Line(quantity: 1, unitPrice: 100, discountPercent: 10, gstRatePercent: 18)]);

        Assert.Equal(100m, totals.SubTotal);
        Assert.Equal(10m, totals.DiscountTotal);
        Assert.Equal(90m, totals.TaxableTotal);
        Assert.Equal(16.2m, totals.TaxTotal);
        Assert.Equal(106m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_SumsMultipleLines()
    {
        var totals = PurchasePricingCalculator.Calculate([
            Line(productId: 1, quantity: 2, unitPrice: 50),
            Line(productId: 2, quantity: 1, unitPrice: 30),
        ]);

        Assert.Equal(130m, totals.SubTotal);
        Assert.Equal(2, totals.Lines.Count);
    }

    [Theory]
    [InlineData(100.40, 100, -0.40)]
    [InlineData(100.60, 101, 0.40)]
    [InlineData(100.50, 101, 0.50)]
    public void Calculate_RoundsGrandTotalToNearestRupee(decimal unitPrice, decimal expectedGrandTotal, decimal expectedRoundOff)
    {
        var totals = PurchasePricingCalculator.Calculate([Line(quantity: 1, unitPrice: unitPrice)]);

        Assert.Equal(expectedGrandTotal, totals.GrandTotal);
        Assert.Equal(expectedRoundOff, totals.RoundOffAmount);
    }

    [Fact]
    public void Calculate_ReturnsZeroTotals_ForEmptyLineList()
    {
        var totals = PurchasePricingCalculator.Calculate([]);

        Assert.Empty(totals.Lines);
        Assert.Equal(0m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_Throws_ForNonPositiveQuantity()
    {
        Assert.Throws<ArgumentException>(() => PurchasePricingCalculator.Calculate([Line(quantity: 0)]));
    }

    [Fact]
    public void Calculate_Throws_ForNegativePrice()
    {
        Assert.Throws<ArgumentException>(() => PurchasePricingCalculator.Calculate([Line(unitPrice: -1)]));
    }

    [Fact]
    public void Calculate_Throws_ForOutOfRangeDiscount()
    {
        Assert.Throws<ArgumentException>(() => PurchasePricingCalculator.Calculate([Line(discountPercent: 101)]));
        Assert.Throws<ArgumentException>(() => PurchasePricingCalculator.Calculate([Line(discountPercent: -1)]));
    }

    [Fact]
    public void Calculate_AllowsDecimalQuantity()
    {
        var totals = PurchasePricingCalculator.Calculate([Line(quantity: 1.5m, unitPrice: 40)]);

        Assert.Equal(60m, totals.SubTotal);
    }
}
