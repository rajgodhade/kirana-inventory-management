using Kirana.Application.Billing;

namespace Kirana.Tests.Billing;

public class CartPricingCalculatorTests
{
    private static CartLine Line(int productId = 1, decimal qty = 1, decimal price = 100, bool inclusive = false, decimal gst = 0, decimal discount = 0) =>
        new()
        {
            ProductId = productId,
            Quantity = qty,
            UnitPrice = price,
            IsTaxInclusive = inclusive,
            GstRatePercent = gst,
            DiscountPercent = discount,
        };

    [Fact]
    public void Calculate_NoDiscountNoGst_TotalsMatchGrossAmount()
    {
        var totals = CartPricingCalculator.Calculate([Line(qty: 2, price: 50)], billDiscountPercent: 0, isGstEnabledForStore: true);

        Assert.Equal(100m, totals.SubTotal);
        Assert.Equal(0m, totals.GstTotal);
        Assert.Equal(100m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_TaxExclusive_AddsGstOnTopOfPrice()
    {
        var totals = CartPricingCalculator.Calculate([Line(qty: 1, price: 100, inclusive: false, gst: 5)], 0, true);

        Assert.Equal(100m, totals.Lines[0].TaxableAmount);
        Assert.Equal(5m, totals.Lines[0].GstAmount);
        Assert.Equal(105m, totals.Lines[0].LineTotal);
        Assert.Equal(105m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_TaxInclusive_BacksOutGstFromPrice()
    {
        // 105 inclusive of 5% GST => taxable 100, gst 5.
        var totals = CartPricingCalculator.Calculate([Line(qty: 1, price: 105, inclusive: true, gst: 5)], 0, true);

        Assert.Equal(100m, totals.Lines[0].TaxableAmount);
        Assert.Equal(5m, totals.Lines[0].GstAmount);
        Assert.Equal(105m, totals.Lines[0].LineTotal);
    }

    [Fact]
    public void Calculate_StoreGstDisabled_IgnoresProductGstRate()
    {
        var totals = CartPricingCalculator.Calculate([Line(qty: 1, price: 100, inclusive: false, gst: 18)], 0, isGstEnabledForStore: false);

        Assert.Equal(0m, totals.GstTotal);
        Assert.Equal(100m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_ItemDiscount_ReducesTaxableAndGstProportionally()
    {
        // 100 gross, 10% item discount => 90 after discount; 5% GST on 90 = 4.50.
        var totals = CartPricingCalculator.Calculate([Line(qty: 1, price: 100, gst: 5, discount: 10)], 0, true);

        Assert.Equal(10m, totals.Lines[0].DiscountAmount);
        Assert.Equal(90m, totals.Lines[0].TaxableAmount);
        Assert.Equal(4.50m, totals.Lines[0].GstAmount);
    }

    [Fact]
    public void Calculate_BillDiscount_ScalesAllLinesProportionally()
    {
        var lines = new[] { Line(productId: 1, qty: 1, price: 100), Line(productId: 2, qty: 1, price: 300) };

        var totals = CartPricingCalculator.Calculate(lines, billDiscountPercent: 10, isGstEnabledForStore: true);

        // Subtotal 400, 10% bill discount = 40 => grand total 360.
        Assert.Equal(400m, totals.SubTotal);
        Assert.Equal(40m, totals.BillDiscountAmount);
        Assert.Equal(360m, totals.GrandTotal);

        // Each line scaled down by the same 90% factor.
        Assert.Equal(90m, totals.Lines[0].TaxableAmount);
        Assert.Equal(270m, totals.Lines[1].TaxableAmount);
    }

    [Theory]
    [InlineData(100.40, 100, -0.40)]
    [InlineData(100.60, 101, 0.40)]
    [InlineData(100.50, 101, 0.50)]
    public void Calculate_RoundsGrandTotalToNearestRupee(decimal price, decimal expectedGrandTotal, decimal expectedRoundOff)
    {
        var totals = CartPricingCalculator.Calculate([Line(qty: 1, price: price)], 0, true);

        Assert.Equal(expectedGrandTotal, totals.GrandTotal);
        Assert.Equal(expectedRoundOff, totals.RoundOffAmount);
    }

    [Fact]
    public void Calculate_MultipleLines_SumsIntoSubTotal()
    {
        var lines = new[] { Line(productId: 1, qty: 2, price: 25), Line(productId: 2, qty: 3, price: 10) };

        var totals = CartPricingCalculator.Calculate(lines, 0, true);

        Assert.Equal(80m, totals.SubTotal);
    }

    [Fact]
    public void Calculate_EmptyCart_ReturnsZeroTotals()
    {
        var totals = CartPricingCalculator.Calculate([], 0, true);

        Assert.Empty(totals.Lines);
        Assert.Equal(0m, totals.SubTotal);
        Assert.Equal(0m, totals.GrandTotal);
    }

    [Fact]
    public void Calculate_Throws_ForNonPositiveQuantity()
    {
        Assert.Throws<ArgumentException>(() => CartPricingCalculator.Calculate([Line(qty: 0)], 0, true));
        Assert.Throws<ArgumentException>(() => CartPricingCalculator.Calculate([Line(qty: -1)], 0, true));
    }

    [Fact]
    public void Calculate_Throws_ForNegativeUnitPrice()
    {
        Assert.Throws<ArgumentException>(() => CartPricingCalculator.Calculate([Line(price: -1)], 0, true));
    }

    [Fact]
    public void Calculate_Throws_ForOutOfRangeItemDiscount()
    {
        Assert.Throws<ArgumentException>(() => CartPricingCalculator.Calculate([Line(discount: 101)], 0, true));
        Assert.Throws<ArgumentException>(() => CartPricingCalculator.Calculate([Line(discount: -1)], 0, true));
    }

    [Fact]
    public void Calculate_Throws_ForOutOfRangeBillDiscount()
    {
        Assert.Throws<ArgumentException>(() => CartPricingCalculator.Calculate([Line()], 101, true));
    }

    [Fact]
    public void Calculate_DecimalQuantity_ScalesLinearly()
    {
        var totals = CartPricingCalculator.Calculate([Line(qty: 1.5m, price: 40)], 0, true);

        Assert.Equal(60m, totals.SubTotal);
    }
}
