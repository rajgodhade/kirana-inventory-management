using Kirana.Application.Taxation;
using Kirana.Domain.Entities;

namespace Kirana.Tests.Taxation;

public sealed class GstJurisdictionResolverTests
{
    private static readonly GstJurisdictionResolver Sut = GstJurisdictionResolver.Shared;

    [Fact]
    public void A_SaleWithSameSnapshotState_IsIntraState() =>
        AssertResolved(Sut.ResolveSale(Sale("27", "27")), GstJurisdiction.IntraState, "27", "27");

    [Fact]
    public void B_SaleWithDifferentSnapshotStates_IsInterState() =>
        AssertResolved(Sut.ResolveSale(Sale("27", "29")), GstJurisdiction.InterState, "27", "29");

    [Fact]
    public void C_SaleWithMissingCustomerState_IsUnresolved() =>
        AssertUnresolved(Sut.ResolveSale(Sale("27", null)), GstJurisdictionUnresolvedReason.MissingCustomerState);

    [Fact]
    public void D_PurchaseWithSameSnapshotState_IsIntraState() =>
        AssertResolved(Sut.ResolvePurchase(Purchase("27", "27")), GstJurisdiction.IntraState, "27", "27");

    [Fact]
    public void E_PurchaseWithDifferentSnapshotStates_IsInterState() =>
        AssertResolved(Sut.ResolvePurchase(Purchase("29", "27")), GstJurisdiction.InterState, "29", "27");

    [Fact]
    public void F_PurchaseWithMissingSupplierState_IsUnresolved() =>
        AssertUnresolved(Sut.ResolvePurchase(Purchase(null, "27")), GstJurisdictionUnresolvedReason.MissingSupplierState);

    [Fact]
    public void G_HistoricalSaleIgnoresLaterCustomerStateEdit()
    {
        var sale = Sale("27", "27");
        sale.Customer = new Customer { StateCode = "29" };

        Assert.Equal(GstJurisdiction.IntraState, Sut.ResolveSale(sale).Jurisdiction);
    }

    [Fact]
    public void H_HistoricalSaleIgnoresLaterCustomerDeletion()
    {
        var sale = Sale("27", "29");
        sale.Customer = null;

        Assert.Equal(GstJurisdiction.InterState, Sut.ResolveSale(sale).Jurisdiction);
    }

    [Fact]
    public void I_HistoricalPurchaseIgnoresLaterSupplierStateEdit()
    {
        var purchase = Purchase("29", "27");
        purchase.Supplier = new Supplier { StateCode = "27" };

        Assert.Equal(GstJurisdiction.InterState, Sut.ResolvePurchase(purchase).Jurisdiction);
    }

    [Fact]
    public void J_HistoricalPurchaseIgnoresLaterSupplierDeletion()
    {
        var purchase = Purchase("27", "27");
        purchase.Supplier = null!;

        Assert.Equal(GstJurisdiction.IntraState, Sut.ResolvePurchase(purchase).Jurisdiction);
    }

    [Fact]
    public void K_WalkInSaleWithoutBuyerState_IsUnresolved()
    {
        var sale = Sale("27", null);
        sale.CustomerId = null;

        AssertUnresolved(Sut.ResolveSale(sale), GstJurisdictionUnresolvedReason.MissingCustomerState);
    }

    [Fact]
    public void L_LegacyTransactionMarkerPreventsGuessing()
    {
        var sale = Sale("27", "27");
        sale.GstIdentitySnapshotCapturedAtUtc = null;

        AssertUnresolved(Sut.ResolveSale(sale), GstJurisdictionUnresolvedReason.LegacyTransaction);
    }

    [Fact]
    public void M_GstinPrefixMismatchDoesNotOverrideStateCodeSnapshots()
    {
        var sale = Sale("27", "27");
        sale.StoreGstinSnapshot = "29ABCDE1234F1Z5";
        sale.CustomerGstinSnapshot = "07ABCDE1234F1Z5";

        Assert.Equal(GstJurisdiction.IntraState, Sut.ResolveSale(sale).Jurisdiction);
    }

    [Fact]
    public void N_ResolutionDoesNotWriteOrMutateTransactionEvidence()
    {
        var sale = Sale(" 27 ", "29");
        var before = (sale.GstIdentitySnapshotCapturedAtUtc, sale.StoreStateCodeSnapshot, sale.CustomerStateCodeSnapshot);

        _ = Sut.ResolveSale(sale);

        Assert.Equal(before, (sale.GstIdentitySnapshotCapturedAtUtc, sale.StoreStateCodeSnapshot, sale.CustomerStateCodeSnapshot));
    }

    [Fact]
    public void O_CurrentStoreMasterCannotOverrideHistoricalStoreSnapshot()
    {
        var sale = Sale("27", "29");
        var currentStore = new Store { StateCode = "29" };
        currentStore.StateCode = "07";

        Assert.Equal(GstJurisdiction.InterState, Sut.ResolveSale(sale).Jurisdiction);
    }

    [Fact]
    public void P_SalesReturnReusesOriginatingSaleJurisdiction()
    {
        var salesReturn = new SalesReturn { Sale = Sale("27", "29") };

        Assert.Equal(GstJurisdiction.InterState, Sut.ResolveSale(salesReturn.Sale).Jurisdiction);
    }

    [Fact]
    public void Q_MultipleReturnsKeepTheSameOriginatingJurisdiction()
    {
        var origin = Purchase("29", "27");
        var returns = new[]
        {
            new PurchaseReturn { Purchase = origin },
            new PurchaseReturn { Purchase = origin },
        };

        Assert.All(returns, item => Assert.Equal(GstJurisdiction.InterState, Sut.ResolvePurchase(item.Purchase).Jurisdiction));
    }

    [Fact]
    public void R_InvalidOrMissingCodesNeverBecomeIntraState()
    {
        Assert.Equal(GstJurisdiction.Unresolved, Sut.ResolveSale(Sale("99", "99")).Jurisdiction);
        Assert.Equal(GstJurisdiction.Unresolved, Sut.ResolvePurchase(Purchase("", "27")).Jurisdiction);
    }

    [Fact]
    public void S_WhitespaceNormalizedSameValidCodes_AreIntraState() =>
        AssertResolved(Sut.ResolveSale(Sale(" 27 ", "27")), GstJurisdiction.IntraState, "27", "27");

    [Fact]
    public void T_DifferentValidCodesRemainInterState() =>
        AssertResolved(Sut.ResolvePurchase(Purchase("07", "27")), GstJurisdiction.InterState, "07", "27");

    [Fact]
    public void MissingStoreStateOnSale_HasSpecificReason() =>
        AssertUnresolved(Sut.ResolveSale(Sale(null, "27")), GstJurisdictionUnresolvedReason.MissingStoreState);

    [Fact]
    public void InvalidCustomerState_HasSpecificReason() =>
        AssertUnresolved(Sut.ResolveSale(Sale("27", "XX")), GstJurisdictionUnresolvedReason.InvalidCustomerState);

    [Fact]
    public void InvalidSupplierState_HasSpecificReason() =>
        AssertUnresolved(Sut.ResolvePurchase(Purchase("XX", "27")), GstJurisdictionUnresolvedReason.InvalidSupplierState);

    [Fact]
    public void MissingStoreStateOnPurchase_HasSpecificReason() =>
        AssertUnresolved(Sut.ResolvePurchase(Purchase("27", null)), GstJurisdictionUnresolvedReason.MissingStoreState);

    private static Sale Sale(string? storeStateCode, string? customerStateCode) => new()
    {
        GstIdentitySnapshotCapturedAtUtc = new DateTime(2026, 8, 20, 1, 2, 3, DateTimeKind.Utc),
        StoreStateCodeSnapshot = storeStateCode,
        CustomerStateCodeSnapshot = customerStateCode,
    };

    private static Purchase Purchase(string? supplierStateCode, string? storeStateCode) => new()
    {
        GstIdentitySnapshotCapturedAtUtc = new DateTime(2026, 8, 20, 1, 2, 3, DateTimeKind.Utc),
        SupplierStateCodeSnapshot = supplierStateCode,
        StoreStateCodeSnapshot = storeStateCode,
    };

    private static void AssertResolved(
        GstJurisdictionResolution resolution,
        GstJurisdiction expected,
        string sellerState,
        string buyerState)
    {
        Assert.True(resolution.IsResolved);
        Assert.Equal(expected, resolution.Jurisdiction);
        Assert.Equal(GstJurisdictionUnresolvedReason.None, resolution.UnresolvedReason);
        Assert.Equal(sellerState, resolution.SellerStateCode);
        Assert.Equal(buyerState, resolution.BuyerStateCode);
    }

    private static void AssertUnresolved(
        GstJurisdictionResolution resolution,
        GstJurisdictionUnresolvedReason expectedReason)
    {
        Assert.False(resolution.IsResolved);
        Assert.Equal(GstJurisdiction.Unresolved, resolution.Jurisdiction);
        Assert.Equal(expectedReason, resolution.UnresolvedReason);
    }
}
