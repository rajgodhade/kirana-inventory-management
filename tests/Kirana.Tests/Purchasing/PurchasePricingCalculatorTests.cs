using Kirana.Application.Purchasing;
using Kirana.Domain.Entities;

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
    public void Calculate_DefaultPricingType_IsInclusive()
    {
        var totals = PurchasePricingCalculator.Calculate([new PurchaseLine
        {
            ProductId = 1,
            Quantity = 1m,
            UnitPrice = 435m,
            GstRatePercent = 5m,
        }]);

        var line = Assert.Single(totals.Lines);
        Assert.Equal(PricingType.Inclusive, line.Line.PricingType);
        Assert.Equal(414.29m, line.TaxableAmount);
        Assert.Equal(20.71m, line.GstAmount);
        Assert.Equal(435m, line.LineTotal);
    }

    [Fact]
    public void Calculate_MixedInclusiveAndExclusiveSupplierPrices()
    {
        var totals = PurchasePricingCalculator.Calculate([
            new PurchaseLine { ProductId = 1, Quantity = 1m, UnitPrice = 105m, GstRatePercent = 5m },
            new PurchaseLine { ProductId = 2, Quantity = 1m, UnitPrice = 100m, GstRatePercent = 5m, PricingType = PricingType.Exclusive },
        ]);

        Assert.Equal(100m, totals.Lines[0].TaxableAmount);
        Assert.Equal(5m, totals.Lines[0].GstAmount);
        Assert.Equal(100m, totals.Lines[1].TaxableAmount);
        Assert.Equal(5m, totals.Lines[1].GstAmount);
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

    // ----------------------------------------------------------------------------------
    // Quantity regression suite. A reported bug had every purchase line priced as if
    // quantity were 1 (10 x ₹210 billed as ₹235.20 instead of ₹2,352). The root cause was
    // in the UI binding rather than here, but these lock down the contract the screen
    // depends on: quantity must scale gross, taxable, GST and the grand total.
    // ----------------------------------------------------------------------------------

    [Fact]
    public void Calculate_QuantityOne_TaxExclusive_ScalesNothing()
    {
        var totals = PurchasePricingCalculator.Calculate([Line(quantity: 1, unitPrice: 210, gstRatePercent: 12)]);

        Assert.Equal(210m, totals.SubTotal);
        Assert.Equal(210m, totals.TaxableTotal);
        Assert.Equal(25.20m, totals.TaxTotal);
        Assert.Equal(235.20m, totals.Lines.Single().LineTotal);
    }

    [Fact]
    public void Calculate_QuantityGreaterThanOne_ScalesGrossTaxableGstAndLineTotal()
    {
        var totals = PurchasePricingCalculator.Calculate([Line(quantity: 10, unitPrice: 210, gstRatePercent: 12)]);

        Assert.Equal(2100m, totals.SubTotal);
        Assert.Equal(2100m, totals.TaxableTotal);
        Assert.Equal(252m, totals.TaxTotal);
        Assert.Equal(2352m, totals.Lines.Single().LineTotal);
        Assert.Equal(2352m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_QuantityChangedFromOneToTen_MultipliesEveryAmountByTen()
    {
        var one = PurchasePricingCalculator.Calculate([Line(quantity: 1, unitPrice: 210, gstRatePercent: 12)]);
        var ten = PurchasePricingCalculator.Calculate([Line(quantity: 10, unitPrice: 210, gstRatePercent: 12)]);

        Assert.Equal(one.SubTotal * 10, ten.SubTotal);
        Assert.Equal(one.TaxableTotal * 10, ten.TaxableTotal);
        Assert.Equal(one.TaxTotal * 10, ten.TaxTotal);
        Assert.Equal(one.Lines.Single().LineTotal * 10, ten.Lines.Single().LineTotal);
    }

    /// <summary>The exact reported scenario: 1 x ₹220 @5% GST plus 10 x ₹210 @12% GST.</summary>
    [Fact]
    public void Calculate_MultipleProductsWithDifferentQuantities_UsesEachLinesOwnQuantity()
    {
        var totals = PurchasePricingCalculator.Calculate([
            Line(productId: 1, quantity: 1, unitPrice: 220, gstRatePercent: 5),
            Line(productId: 2, quantity: 10, unitPrice: 210, gstRatePercent: 12),
        ]);

        var atta = totals.Lines.Single(l => l.Line.ProductId == 1);
        var butter = totals.Lines.Single(l => l.Line.ProductId == 2);

        Assert.Equal(220m, atta.GrossAmount);
        Assert.Equal(11m, atta.GstAmount);
        Assert.Equal(231m, atta.LineTotal);

        Assert.Equal(2100m, butter.GrossAmount);
        Assert.Equal(252m, butter.GstAmount);
        Assert.Equal(2352m, butter.LineTotal);

        Assert.Equal(2320m, totals.SubTotal);
        Assert.Equal(263m, totals.TaxTotal);
        Assert.Equal(2583m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_QuantityScales_WithDiscountAndInclusiveTax()
    {
        var totals = PurchasePricingCalculator.Calculate([
            Line(quantity: 4, unitPrice: 112, isTaxInclusive: true, gstRatePercent: 12, discountPercent: 10),
        ]);

        // 4 x 112 = 448 gross, less 10% = 403.20 inclusive, back-split at 12%.
        Assert.Equal(448m, totals.SubTotal);
        Assert.Equal(44.80m, totals.DiscountTotal);
        Assert.Equal(360m, totals.TaxableTotal);
        Assert.Equal(43.20m, totals.TaxTotal);
    }
}
