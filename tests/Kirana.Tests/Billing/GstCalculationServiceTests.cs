using Kirana.Application.Billing;
using Kirana.Application.Taxation;
using Kirana.Domain.Entities;

namespace Kirana.Tests.Billing;

public sealed class GstCalculationServiceTests
{
    private readonly GstCalculationService _sut = new();

    private static CartLine Line(
        int id, decimal price, decimal rate, decimal quantity = 1m, decimal itemDiscount = 0m,
        decimal promotion = 0m, bool inclusive = false) => new()
    {
        ProductId = id,
        Quantity = quantity,
        UnitPrice = price,
        GstRatePercent = rate,
        DiscountPercent = itemDiscount,
        PromotionBeforeTaxDiscountAmount = promotion,
        IsTaxInclusive = inclusive,
    };

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 5)]
    [InlineData(12, 12)]
    [InlineData(18, 18)]
    [InlineData(28, 28)]
    public void SingleSlab_UsesOnlyThatRate(decimal rate, decimal expectedTax)
    {
        var result = _sut.CalculateSales([Line(1, 100m, rate)], 0m, true);
        Assert.Equal(expectedTax, result.GstTotal);
        Assert.Equal(100m, result.TaxableTotal);
    }

    [Fact]
    public void MixedSlabs_AreCalculatedIndependently()
    {
        var result = _sut.CalculateSales([
            Line(1, 420m, 5m),
            Line(2, 284m, 12m),
            Line(3, 212m, 18m),
        ], 0m, true);

        Assert.Collection(result.Lines,
            line => Assert.Equal(21m, line.GstAmount),
            line => Assert.Equal(34.08m, line.GstAmount),
            line => Assert.Equal(38.16m, line.GstAmount));
        Assert.Equal(93.24m, result.GstTotal);
    }

    [Fact]
    public void ItemThenPromotionThenBillDiscount_PrecedeGst()
    {
        var result = _sut.CalculateSales([Line(1, 100m, 18m, itemDiscount: 10m, promotion: 10m)], 10m, true);
        Assert.Equal(72m, result.TaxableTotal);
        Assert.Equal(12.96m, result.GstTotal);
        Assert.Equal(10m, result.ItemDiscountTotal);
        Assert.Equal(10m, result.PromotionDiscountTotal);
        Assert.Equal(8m, result.BillDiscountAmount);
    }

    [Fact]
    public void PromotionAcrossMixedSlabs_ReducesEachAffectedSlabBeforeTax()
    {
        var result = _sut.CalculateSales([
            Line(1, 100m, 5m, promotion: 10m),
            Line(2, 300m, 18m, promotion: 30m),
        ], 0m, true);

        Assert.Equal(90m, result.Lines[0].TaxableAmount);
        Assert.Equal(4.50m, result.Lines[0].GstAmount);
        Assert.Equal(270m, result.Lines[1].TaxableAmount);
        Assert.Equal(48.60m, result.Lines[1].GstAmount);
    }

    [Fact]
    public void FlatFestivalAndFreeItemEquivalentPromotions_AllReduceTaxableValue()
    {
        var flat = _sut.CalculateSales([Line(1, 100m, 12m, promotion: 15m)], 0m, true);
        var festival = _sut.CalculateSales([Line(1, 100m, 12m, promotion: 20m)], 0m, true);
        var buyOneGetOne = _sut.CalculateSales([Line(1, 50m, 5m, quantity: 2m, promotion: 50m)], 0m, true);

        Assert.Equal(85m, flat.TaxableTotal);
        Assert.Equal(80m, festival.TaxableTotal);
        Assert.Equal(50m, buyOneGetOne.TaxableTotal);
        Assert.Equal(2.50m, buyOneGetOne.GstTotal);
    }

    [Fact]
    public void TaxInclusiveDiscount_BackCalculatesTaxOnlyAfterDiscounts()
    {
        var result = _sut.CalculateSales([Line(1, 118m, 18m, itemDiscount: 10m, promotion: 11.80m, inclusive: true)], 0m, true);
        Assert.Equal(80m, result.TaxableTotal);
        Assert.Equal(14.40m, result.GstTotal);
    }

    [Fact]
    public void GstInclusive_DefaultMode_ExtractsTaxAndNeverAddsItAgain()
    {
        var result = _sut.CalculateSales([new CartLine
        {
            ProductId = 1,
            Quantity = 1m,
            UnitPrice = 435m,
            GstRatePercent = 5m,
        }], 0m, true);

        var line = Assert.Single(result.Lines);
        Assert.Equal(PricingType.Inclusive, line.Line.PricingType);
        Assert.Equal(414.29m, line.TaxableAmount);
        Assert.Equal(20.71m, line.GstAmount);
        Assert.Equal(435m, line.LineTotal);
        Assert.Equal(435m, result.GrandTotal);
    }

    [Fact]
    public void GstExclusive_AddsTaxToDiscountedSellingPrice()
    {
        var result = _sut.CalculateSales([new CartLine
        {
            ProductId = 1,
            Quantity = 1m,
            UnitPrice = 435m,
            GstRatePercent = 5m,
            PricingType = PricingType.Exclusive,
        }], 0m, true);

        var line = Assert.Single(result.Lines);
        Assert.Equal(435m, line.TaxableAmount);
        Assert.Equal(21.75m, line.GstAmount);
        Assert.Equal(456.75m, line.LineTotal);
    }

    [Fact]
    public void MixedPricingModes_UseEachLinesOwnTreatment()
    {
        var result = _sut.CalculateSales([
            new CartLine { ProductId = 1, Quantity = 1m, UnitPrice = 105m, GstRatePercent = 5m },
            new CartLine { ProductId = 2, Quantity = 1m, UnitPrice = 100m, GstRatePercent = 5m, PricingType = PricingType.Exclusive },
        ], 0m, true);

        Assert.Equal(100m, result.Lines[0].TaxableAmount);
        Assert.Equal(105m, result.Lines[0].LineTotal);
        Assert.Equal(100m, result.Lines[1].TaxableAmount);
        Assert.Equal(105m, result.Lines[1].LineTotal);
    }

    [Fact]
    public void InclusivePromotionsItemAndBillDiscounts_ReducePriceBeforeTaxExtraction()
    {
        var result = _sut.CalculateSales([new CartLine
        {
            ProductId = 1,
            Quantity = 1m,
            UnitPrice = 118m,
            GstRatePercent = 18m,
            DiscountPercent = 10m,
            PromotionBeforeTaxDiscountAmount = 6.20m,
        }], 10m, true);

        var line = Assert.Single(result.Lines);
        Assert.Equal(90m, line.LineTotal);
        Assert.Equal(76.27m, line.TaxableAmount);
        Assert.Equal(13.73m, line.GstAmount);
    }

    [Fact]
    public void ZeroAndExemptProducts_HaveNoGst()
    {
        var result = _sut.CalculateSales([Line(1, 50m, 0m), Line(2, 75m, 0m)], 0m, true);
        Assert.Equal(125m, result.TaxableTotal);
        Assert.Equal(0m, result.GstTotal);
    }

    [Fact]
    public void StoredSummary_GroupsSnapshotsAndCountsDistinctInvoices()
    {
        var summary = _sut.SummarizeStored([
            new GstSnapshotLine { TransactionId = 1, RatePercent = 5m, TaxableAmount = 100m, GstAmount = 5m },
            new GstSnapshotLine { TransactionId = 1, RatePercent = 5m, TaxableAmount = 50m, GstAmount = 2.5m },
            new GstSnapshotLine { TransactionId = 2, RatePercent = 18m, TaxableAmount = 100m, GstAmount = 18m },
        ]);

        Assert.Equal(2, summary.Count);
        Assert.Equal(1, summary.Single(x => x.RatePercent == 5m).InvoiceCount);
        Assert.Equal(150m, summary.Single(x => x.RatePercent == 5m).TaxableAmount);
    }

    [Fact]
    public void StoredSummary_DoesNotMergeIncludedAndAddedTaxAtTheSameRate()
    {
        var summary = _sut.SummarizeStored([
            new GstSnapshotLine { TransactionId = 1, RatePercent = 5m, TaxableAmount = 100m, GstAmount = 5m, PricingType = PricingType.Inclusive },
            new GstSnapshotLine { TransactionId = 1, RatePercent = 5m, TaxableAmount = 100m, GstAmount = 5m, PricingType = PricingType.Exclusive },
        ]);

        Assert.Equal(2, summary.Count);
        Assert.Contains(summary, x => x.PricingType == PricingType.Inclusive);
        Assert.Contains(summary, x => x.PricingType == PricingType.Exclusive);
    }

    [Fact]
    public void FinalTotals_PassTaxableTaxAndRoundOffInvariants()
    {
        var result = _sut.CalculateSales([Line(1, 99.99m, 18m), Line(2, 47.25m, 5m)], 3m, true);
        var snapshots = result.Lines.Select(x => new GstSnapshotLine
        {
            TransactionId = 1,
            RatePercent = x.Line.GstRatePercent,
            TaxableAmount = x.TaxableAmount,
            GstAmount = x.GstAmount,
        }).ToList();

        Assert.True(_sut.ValidateStored(snapshots, new GstStoredTotals
        {
            TaxableTotal = result.TaxableTotal,
            GstTotal = result.GstTotal,
            RoundOffAmount = result.RoundOffAmount,
            GrandTotal = result.GrandTotal,
        }, "test"));
    }

    [Fact]
    public void UnsupportedProductRate_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => _sut.CalculateSales([Line(1, 100m, 7m)], 0m, true));
    }
}
