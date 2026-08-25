using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;

namespace Kirana.Tests.Taxation;

/// <summary>Phase 18A-5 matrix for the centralized GST component calculator: intra/inter-state
/// splits across every supported slab, decimal taxable values, typed unresolved outcomes, and the
/// invariants that keep a genuine zero-GST line distinct from a calculation that could not be
/// resolved.</summary>
public sealed class GstTaxCalculatorTests
{
    private static readonly GstTaxCalculator Sut = GstTaxCalculator.Shared;
    private static readonly GstTaxContextResolver ContextResolver = GstTaxContextResolver.Shared;

    public static TheoryData<decimal> SupportedRates => [0m, 5m, 12m, 18m, 28m];

    // ------------------------------------------------------------ A/E + F-I: intra-state slabs

    [Theory]
    [MemberData(nameof(SupportedRates))]
    public void IntraState_TaxableValue_SplitsEvenlyIntoCgstAndSgst(decimal rate)
    {
        var result = Sut.Calculate(SaleContext(store: "27", customer: "27"), taxableValue: 1000m, rate);

        Assert.True(result.IsResolved);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
        Assert.Equal(1000m, result.TaxableValue);
        Assert.Equal(rate, result.GstRate);
        var expectedGst = GstRound(1000m * rate / 100m);
        Assert.Equal(expectedGst, result.TotalGst);
        Assert.Equal(GstRound(expectedGst / 2m), result.Cgst);
        Assert.Equal(expectedGst - result.Cgst, result.Sgst);
        Assert.Equal(0m, result.Igst);
        Assert.True(result.ComponentsReconcile);
    }

    // ------------------------------------------------------------ B/F-J: inter-state slabs

    [Theory]
    [MemberData(nameof(SupportedRates))]
    public void InterState_TaxableValue_GoesEntirelyToIgst(decimal rate)
    {
        var result = Sut.Calculate(SaleContext(store: "27", customer: "29"), taxableValue: 1000m, rate);

        Assert.True(result.IsResolved);
        Assert.Equal(GstJurisdiction.InterState, result.Jurisdiction);
        Assert.Equal(rate, result.GstRate);
        var expectedGst = GstRound(1000m * rate / 100m);
        Assert.Equal(expectedGst, result.Igst);
        Assert.Equal(0m, result.Cgst);
        Assert.Equal(0m, result.Sgst);
        Assert.Equal(expectedGst, result.TotalGst);
        Assert.True(result.ComponentsReconcile);
    }

    // ------------------------------------------------------------ K: decimal taxable values

    [Fact]
    public void DecimalTaxable_IntraState_RoundsByTheSharedFinancialPolicy()
    {
        // 123.45 @ 18% = 22.221 → 22.22; half = 11.11 exactly.
        var result = Sut.Calculate(SaleContext("27", "27"), 123.45m, 18m);

        Assert.Equal(22.22m, result.TotalGst);
        Assert.Equal(11.11m, result.Cgst);
        Assert.Equal(11.11m, result.Sgst);
    }

    [Fact]
    public void OddPaise_IntraState_CgstRoundsAwayFromZero_AndSgstTakesTheRemainder()
    {
        // 33.33 @ 5% = 1.6665 → 1.67 total; half = 0.835 → CGST rounds away from zero to 0.84,
        // SGST keeps the exact remainder so the components always reconcile.
        var result = Sut.Calculate(SaleContext("27", "27"), 33.33m, 5m);

        Assert.Equal(1.67m, result.TotalGst);
        Assert.Equal(0.84m, result.Cgst);
        Assert.Equal(0.83m, result.Sgst);
        Assert.True(result.ComponentsReconcile);
    }

    // ------------------------------------------------------------ L: discounted taxable value

    [Fact]
    public void DiscountedTaxableValue_IsWhatTheRateAppliesTo()
    {
        // A ₹1,000 line after a 10% discount carries a ₹900 taxable value; the calculator must tax
        // exactly that — it never re-derives discounts itself.
        var intraResult = Sut.Calculate(SaleContext("27", "27"), 900m, 18m);
        var interResult = Sut.Calculate(SaleContext("27", "29"), 900m, 18m);

        Assert.Equal(162m, intraResult.TotalGst);
        Assert.Equal(81m, intraResult.Cgst);
        Assert.Equal(81m, intraResult.Sgst);
        Assert.Equal(162m, interResult.Igst);
    }

    // ------------------------------------------------------------ C/D/G + step 7: unresolved inputs

    [Fact]
    public void MissingSellerState_IsTypedUnresolved_NeverAssumedIntraOrInter()
    {
        var result = Sut.Calculate(SaleContext(null, "27"), 1000m, 18m);

        Assert.False(result.IsResolved);
        Assert.Equal(GstJurisdiction.Unresolved, result.Jurisdiction);
        Assert.Equal(GstJurisdictionUnresolvedReason.MissingStoreState, result.UnresolvedReason);
        Assert.Equal(0m, result.Cgst);
        Assert.Equal(0m, result.Sgst);
        Assert.Equal(0m, result.Igst);
        Assert.Equal(0m, result.TotalGst);
    }

    [Fact]
    public void MissingBuyerState_WalkInSale_IsTypedUnresolved()
    {
        var sale = SaleEntity("27", null);
        sale.CustomerId = null;

        var result = Sut.Calculate(ContextResolver.ResolveSale(sale), 1000m, 18m);

        Assert.False(result.IsResolved);
        Assert.Equal(GstJurisdictionUnresolvedReason.MissingCustomerState, result.UnresolvedReason);
        Assert.Equal(0m, result.TotalGst);
    }

    [Fact]
    public void InvalidHistoricalStateCode_IsTypedUnresolved()
    {
        var result = Sut.Calculate(SaleContext("27", "XX"), 1000m, 18m);

        Assert.False(result.IsResolved);
        Assert.Equal(GstJurisdictionUnresolvedReason.InvalidCustomerState, result.UnresolvedReason);
    }

    // ------------------------------------------------------------ E: legacy transactions

    [Fact]
    public void LegacyTransactionWithoutCaptureMarker_IsTypedUnresolved()
    {
        var sale = SaleEntity("27", "27");
        sale.GstIdentitySnapshotCapturedAtUtc = null;

        var result = Sut.Calculate(ContextResolver.ResolveSale(sale), 1000m, 18m);

        Assert.False(result.IsResolved);
        Assert.Equal(GstJurisdictionUnresolvedReason.LegacyTransaction, result.UnresolvedReason);
        Assert.Equal(GstJurisdiction.Unresolved, result.Jurisdiction);
        Assert.All([result.Cgst, result.Sgst, result.Igst, result.TotalGst], amount => Assert.Equal(0m, amount));
    }

    // ------------------------------------------------------------ The zero-vs-unresolved distinction

    [Fact]
    public void ResolvedZeroPercentLine_IsNotConfusedWithAnUnresolvedCalculation()
    {
        var exempt = Sut.Calculate(SaleContext("27", "27"), 1000m, 0m);
        var legacySale = SaleEntity("27", "29");
        legacySale.GstIdentitySnapshotCapturedAtUtc = null;
        var unresolved = Sut.Calculate(ContextResolver.ResolveSale(legacySale), 1000m, 0m);

        Assert.True(exempt.IsResolved);
        Assert.Equal(GstJurisdictionUnresolvedReason.None, exempt.UnresolvedReason);
        Assert.True(exempt.ComponentsReconcile);

        Assert.False(unresolved.IsResolved);
        Assert.Equal(GstJurisdictionUnresolvedReason.LegacyTransaction, unresolved.UnresolvedReason);
        Assert.NotEqual(exempt.IsResolved, unresolved.IsResolved);
        Assert.NotEqual(exempt.UnresolvedReason, unresolved.UnresolvedReason);
    }

    // ------------------------------------------------------------ R/S/T: classification never drives arithmetic

    [Fact]
    public void B2B_B2C_AndUnresolvedClassifications_ProduceIdenticalSplitsForTheSameJurisdiction()
    {
        var b2bSale = SaleEntity("27", "29");
        b2bSale.CustomerId = 1;
        b2bSale.CustomerGstRegistrationTypeSnapshot = GstRegistrationType.Regular;
        b2bSale.CustomerGstinSnapshot = "29AAACB2894G1ZJ";

        var b2cSale = SaleEntity("27", "29");
        b2cSale.CustomerId = 1;
        b2cSale.CustomerGstRegistrationTypeSnapshot = GstRegistrationType.Unregistered;
        b2cSale.CustomerGstinSnapshot = null;

        var unresolvedSale = SaleEntity("27", "29");
        unresolvedSale.CustomerId = 1;
        unresolvedSale.CustomerGstRegistrationTypeSnapshot = null;
        unresolvedSale.CustomerGstinSnapshot = null;

        var b2b = Sut.Calculate(ContextResolver.ResolveSale(b2bSale), 1000m, 18m);
        var b2c = Sut.Calculate(ContextResolver.ResolveSale(b2cSale), 1000m, 18m);
        var unresolvedClass = Sut.Calculate(ContextResolver.ResolveSale(unresolvedSale), 1000m, 18m);

        Assert.Equal(GstTransactionClass.B2B, ContextResolver.ResolveSale(b2bSale).Classification);
        Assert.Equal(GstTransactionClass.B2C, ContextResolver.ResolveSale(b2cSale).Classification);
        Assert.Equal(GstTransactionClass.Unresolved, ContextResolver.ResolveSale(unresolvedSale).Classification);

        // B2B/B2C is classification context only — GST arithmetic is identical.
        foreach (var result in new[] { b2b, b2c, unresolvedClass })
        {
            Assert.True(result.IsResolved);
            Assert.Equal(GstJurisdiction.InterState, result.Jurisdiction);
            Assert.Equal(180m, result.Igst);
            Assert.Equal(0m, result.Cgst);
            Assert.Equal(0m, result.Sgst);
            Assert.True(result.ComponentsReconcile);
        }
    }

    // ------------------------------------------------------------ Step 9: purchase contexts

    [Fact]
    public void Purchase_IntraState_SplitsLikeSales()
    {
        var result = Sut.Calculate(PurchaseContext(supplier: "27", store: "27"), 1000m, 18m);

        Assert.True(result.IsResolved);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
        Assert.Equal(90m, result.Cgst);
        Assert.Equal(90m, result.Sgst);
        Assert.Equal(0m, result.Igst);
    }

    [Fact]
    public void Purchase_InterState_GoesToIgst()
    {
        var result = Sut.Calculate(PurchaseContext(supplier: "29", store: "27"), 1000m, 18m);

        Assert.True(result.IsResolved);
        Assert.Equal(180m, result.Igst);
        Assert.Equal(0m, result.Cgst);
        Assert.Equal(0m, result.Sgst);
    }

    [Fact]
    public void Purchase_MissingSupplierState_IsTypedUnresolved()
    {
        var result = Sut.Calculate(PurchaseContext(null, "27"), 1000m, 18m);

        Assert.False(result.IsResolved);
        Assert.Equal(GstJurisdictionUnresolvedReason.MissingSupplierState, result.UnresolvedReason);
    }

    // ------------------------------------------------------------ SplitStored: stored totals stay authoritative

    [Theory]
    [InlineData(180.00, 90.00, 90.00)]
    [InlineData(13.33, 6.67, 6.66)]
    [InlineData(0.03, 0.02, 0.01)]
    [InlineData(0.01, 0.01, 0.00)]
    public void SplitStored_IntraState_AllocatesEveryPaise(double total, double cgst, double sgst)
    {
        var result = Sut.SplitStored(SaleContext("27", "27"), taxableValue: 1000m, (decimal)total);

        Assert.True(result.IsResolved);
        Assert.Equal((decimal)total, result.TotalGst);
        Assert.Equal((decimal)cgst, result.Cgst);
        Assert.Equal((decimal)sgst, result.Sgst);
        Assert.Equal(0m, result.Igst);
        Assert.True(result.ComponentsReconcile);
    }

    [Fact]
    public void SplitStored_InterState_KeepsTheStoredTotalAsIgst()
    {
        var result = Sut.SplitStored(SaleContext("27", "29"), 1000m, 117.37m);

        Assert.True(result.IsResolved);
        Assert.Equal(117.37m, result.Igst);
        Assert.Equal(0m, result.Cgst);
        Assert.Equal(0m, result.Sgst);
    }

    [Fact]
    public void SplitStored_OfAnUnresolvedTransaction_YieldsTheTypedUnresolvedResult()
    {
        var sale = SaleEntity("27", "27");
        sale.GstIdentitySnapshotCapturedAtUtc = null;

        var result = Sut.SplitStored(ContextResolver.ResolveSale(sale), 1000m, 42.50m);

        Assert.False(result.IsResolved);
        Assert.Equal(GstJurisdictionUnresolvedReason.LegacyTransaction, result.UnresolvedReason);
        Assert.Equal(0m, result.TotalGst);
    }

    [Fact]
    public void SplitStored_ReportsNoRate_BecauseItNeverAppliesOne()
    {
        var result = Sut.SplitStored(SaleContext("27", "27"), 1000m, 180m);

        Assert.True(result.IsResolved);
        Assert.Equal(0m, result.GstRate);
    }

    // ------------------------------------------------------------ U: purity — calculation mutates nothing

    [Fact]
    public void Calculation_PerformsNoWrites_AndMutatesNothing()
    {
        var sale = SaleEntity("27", "29");
        sale.CustomerId = 7;
        sale.StoreTradeNameSnapshot = "Kirana Store";
        sale.CustomerGstinSnapshot = "29AAACB2894G1ZJ";
        sale.CustomerGstRegistrationTypeSnapshot = GstRegistrationType.Regular;
        var context = ContextResolver.ResolveSale(sale);
        var before = (
            sale.StoreStateCodeSnapshot, sale.CustomerStateCodeSnapshot,
            sale.CustomerGstinSnapshot, sale.CustomerGstRegistrationTypeSnapshot,
            sale.GstIdentitySnapshotCapturedAtUtc, context.Classification, context.Jurisdiction);

        Sut.Calculate(context, 999.99m, 28m);
        Sut.SplitStored(context, 999.99m, 12.34m);

        var after = (
            sale.StoreStateCodeSnapshot, sale.CustomerStateCodeSnapshot,
            sale.CustomerGstinSnapshot, sale.CustomerGstRegistrationTypeSnapshot,
            sale.GstIdentitySnapshotCapturedAtUtc, context.Classification, context.Jurisdiction);
        Assert.Equal(before, after);
    }

    // ------------------------------------------------------------ Guards

    [Theory]
    [InlineData(14)]
    [InlineData(-5)]
    public void UnsupportedOrNegativeRates_AreRejected_ByTheExistingRatePolicy(double rate)
    {
        Assert.Throws<ArgumentException>(
            () => Sut.Calculate(SaleContext("27", "27"), 1000m, (decimal)rate));
    }

    [Fact]
    public void NegativeTaxableValue_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Sut.Calculate(SaleContext("27", "27"), -1m, 18m));
    }

    [Fact]
    public void NegativeStoredTotal_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Sut.SplitStored(SaleContext("27", "27"), 1000m, -0.01m));
    }

    // ------------------------------------------------------------ builders

    private static GstTaxContext SaleContext(string? store, string? customer) =>
        ContextResolver.ResolveSale(SaleEntity(store, customer));

    private static Sale SaleEntity(string? storeStateCode, string? customerStateCode) => new()
    {
        GstIdentitySnapshotCapturedAtUtc = new DateTime(2026, 8, 20, 1, 2, 3, DateTimeKind.Utc),
        StoreStateCodeSnapshot = storeStateCode,
        CustomerStateCodeSnapshot = customerStateCode,
    };

    private static GstPurchaseTaxContext PurchaseContext(string? supplier, string? store)
    {
        var purchase = new Purchase
        {
            GstIdentitySnapshotCapturedAtUtc = new DateTime(2026, 8, 20, 1, 2, 3, DateTimeKind.Utc),
            SupplierStateCodeSnapshot = supplier,
            StoreStateCodeSnapshot = store,
        };
        return ContextResolver.ResolvePurchase(purchase);
    }

    private static decimal GstRound(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
