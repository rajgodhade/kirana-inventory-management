using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;

namespace Kirana.Tests.Taxation;

public sealed class GstTransactionClassifierTests
{
    private const string MaharashtraGstin = "27AAPFU0939F1ZV";
    private const string KarnatakaGstin = "29AAACB2894G1ZJ";
    private static readonly DateTime CapturedAt = new(2026, 8, 21, 1, 2, 3, DateTimeKind.Utc);
    private static readonly GstTransactionClassifier Sut = GstTransactionClassifier.Shared;
    private static readonly GstTaxContextResolver ContextResolver = GstTaxContextResolver.Shared;

    [Fact]
    public void RegularCustomerWithValidHistoricalIdentity_IsB2B() =>
        AssertSale(Sut.ClassifySale(Sale(GstRegistrationType.Regular)), GstTransactionClass.B2B);

    [Fact]
    public void CompositionCustomerWithValidHistoricalIdentity_IsB2B() =>
        AssertSale(Sut.ClassifySale(Sale(GstRegistrationType.Composition)), GstTransactionClass.B2B);

    [Fact]
    public void UnregisteredCustomer_IsB2C() =>
        AssertSale(Sut.ClassifySale(Sale(GstRegistrationType.Unregistered, gstin: null)), GstTransactionClass.B2C);

    [Fact]
    public void NullRegistrationType_IsUnresolved()
    {
        var result = Sut.ClassifySale(Sale(null));

        AssertSale(result, GstTransactionClass.Unresolved);
        Assert.Equal(GstIdentityClassificationReason.MissingRegistrationType, result.Reason);
    }

    [Fact]
    public void LegacySaleWithoutHistoricalCapture_IsUnresolvedEvenWhenSnapshotsLookComplete()
    {
        var sale = Sale(GstRegistrationType.Regular);
        sale.GstIdentitySnapshotCapturedAtUtc = null;

        var result = Sut.ClassifySale(sale);

        AssertSale(result, GstTransactionClass.Unresolved);
        Assert.Equal(GstIdentityClassificationReason.LegacyTransaction, result.Reason);
    }

    [Fact]
    public void RegisteredCustomerWithoutHistoricalGstin_IsUnresolved()
    {
        var result = Sut.ClassifySale(Sale(GstRegistrationType.Regular, gstin: null));

        AssertSale(result, GstTransactionClass.Unresolved);
        Assert.Equal(GstIdentityClassificationReason.MissingGstin, result.Reason);
    }

    [Fact]
    public void RegisteredCustomerWithInvalidHistoricalGstin_IsUnresolved()
    {
        var result = Sut.ClassifySale(Sale(GstRegistrationType.Regular, gstin: "27INVALIDGSTIN"));

        AssertSale(result, GstTransactionClass.Unresolved);
        Assert.Equal(GstIdentityClassificationReason.InvalidGstin, result.Reason);
    }

    [Fact]
    public void RegisteredCustomerWithGstinStateMismatch_IsUnresolved()
    {
        var result = Sut.ClassifySale(Sale(
            GstRegistrationType.Regular,
            customerState: "29",
            gstin: MaharashtraGstin));

        AssertSale(result, GstTransactionClass.Unresolved);
        Assert.Equal(GstIdentityClassificationReason.InvalidGstin, result.Reason);
    }

    [Fact]
    public void ExplicitCapturedWalkInSale_IsB2C()
    {
        var sale = Sale(null, customerPresent: false, gstin: null);

        var result = Sut.ClassifySale(sale);

        AssertSale(result, GstTransactionClass.B2C);
        Assert.Equal(GstIdentityClassificationReason.ExplicitWalkInCustomer, result.Reason);
    }

    [Fact]
    public void CustomerRegistrationEdit_DoesNotChangeHistoricalSale()
    {
        var customer = Customer(GstRegistrationType.Regular);
        var sale = CaptureSale(customer);
        customer.GstRegistrationType = GstRegistrationType.Unregistered;

        AssertSale(Sut.ClassifySale(sale), GstTransactionClass.B2B);
    }

    [Fact]
    public void CustomerGstinEdit_DoesNotChangeHistoricalSale()
    {
        var customer = Customer(GstRegistrationType.Regular);
        var sale = CaptureSale(customer);
        customer.Gstin = null;

        AssertSale(Sut.ClassifySale(sale), GstTransactionClass.B2B);
    }

    [Fact]
    public void CustomerStateEdit_DoesNotChangeHistoricalSaleOrJurisdiction()
    {
        var customer = Customer(GstRegistrationType.Regular);
        var sale = CaptureSale(customer);
        customer.StateCode = "29";

        var result = ContextResolver.ResolveSale(sale);

        Assert.Equal(GstTransactionClass.B2B, result.Classification);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
    }

    [Fact]
    public void CustomerNameAndAddressEdit_DoNotCreateOrChangeClassification()
    {
        var customer = Customer(GstRegistrationType.Regular);
        var sale = CaptureSale(customer);
        customer.Name = "Changed Private Limited";
        customer.Address = "Changed address";

        AssertSale(Sut.ClassifySale(sale), GstTransactionClass.B2B);
    }

    [Fact]
    public void StoreIdentityEdit_DoesNotChangeHistoricalSaleContext()
    {
        var store = Store();
        var sale = CaptureSale(Customer(GstRegistrationType.Regular), store);
        store.GstRegistrationType = GstRegistrationType.Unregistered;
        store.Gstin = null;
        store.StateCode = "29";
        store.LegalName = "Changed Legal Name";

        var result = ContextResolver.ResolveSale(sale);

        Assert.Equal(GstTransactionClass.B2B, result.Classification);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
    }

    [Fact]
    public void SupplierRegistrationEdit_DoesNotChangeHistoricalPurchase()
    {
        var supplier = Supplier(GstRegistrationType.Regular);
        var purchase = CapturePurchase(supplier);
        supplier.GstRegistrationType = GstRegistrationType.Unregistered;

        Assert.Equal(GstPurchasePartyClass.RegisteredSupplier, Sut.ClassifyPurchase(purchase).Classification);
    }

    [Fact]
    public void SupplierGstinEdit_DoesNotChangeHistoricalPurchase()
    {
        var supplier = Supplier(GstRegistrationType.Regular);
        var purchase = CapturePurchase(supplier);
        supplier.Gstin = null;

        Assert.Equal(GstPurchasePartyClass.RegisteredSupplier, Sut.ClassifyPurchase(purchase).Classification);
    }

    [Fact]
    public void SupplierStateAndNameEdit_DoNotChangeHistoricalPurchaseContext()
    {
        var supplier = Supplier(GstRegistrationType.Regular);
        var purchase = CapturePurchase(supplier);
        supplier.StateCode = "29";
        supplier.Name = "Changed Supplier";

        var result = ContextResolver.ResolvePurchase(purchase);

        Assert.Equal(GstPurchasePartyClass.RegisteredSupplier, result.SupplierClassification);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
    }

    [Fact]
    public void B2BAndSameStateSnapshots_ResolveTogether()
    {
        var result = ContextResolver.ResolveSale(Sale(GstRegistrationType.Regular));

        Assert.Equal(GstTransactionClass.B2B, result.Classification);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
    }

    [Fact]
    public void B2BAndDifferentStateSnapshots_ResolveTogether()
    {
        var result = ContextResolver.ResolveSale(Sale(
            GstRegistrationType.Regular,
            customerState: "29",
            gstin: KarnatakaGstin));

        Assert.Equal(GstTransactionClass.B2B, result.Classification);
        Assert.Equal(GstJurisdiction.InterState, result.Jurisdiction);
    }

    [Fact]
    public void B2CAndSameStateSnapshots_ResolveTogether()
    {
        var result = ContextResolver.ResolveSale(Sale(GstRegistrationType.Unregistered, gstin: null));

        Assert.Equal(GstTransactionClass.B2C, result.Classification);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
    }

    [Fact]
    public void B2CAndDifferentStateSnapshots_ResolveTogether()
    {
        var result = ContextResolver.ResolveSale(Sale(
            GstRegistrationType.Unregistered,
            customerState: "29",
            gstin: null));

        Assert.Equal(GstTransactionClass.B2C, result.Classification);
        Assert.Equal(GstJurisdiction.InterState, result.Jurisdiction);
    }

    [Fact]
    public void UnresolvedClassificationCanRetainResolvedJurisdiction()
    {
        var result = ContextResolver.ResolveSale(Sale(null));

        Assert.Equal(GstTransactionClass.Unresolved, result.Classification);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
    }

    [Fact]
    public void ResolvedClassificationCanRetainUnresolvedJurisdiction()
    {
        var result = ContextResolver.ResolveSale(Sale(
            GstRegistrationType.Regular,
            customerState: null,
            gstin: MaharashtraGstin));

        Assert.Equal(GstTransactionClass.B2B, result.Classification);
        Assert.Equal(GstJurisdiction.Unresolved, result.Jurisdiction);
    }

    [Fact]
    public void LegacySaleIsFullyUnresolvedWithoutCurrentMasterFallback()
    {
        var sale = Sale(GstRegistrationType.Regular);
        sale.GstIdentitySnapshotCapturedAtUtc = null;
        sale.Customer = Customer(GstRegistrationType.Regular);

        var result = ContextResolver.ResolveSale(sale);

        Assert.Equal(GstTransactionClass.Unresolved, result.Classification);
        Assert.Equal(GstJurisdiction.Unresolved, result.Jurisdiction);
    }

    [Fact]
    public void B2BSalesReturn_InheritsOriginClassification()
    {
        var result = Sut.ClassifySalesReturn(new SalesReturn { Sale = Sale(GstRegistrationType.Regular) });

        AssertSale(result, GstTransactionClass.B2B);
    }

    [Fact]
    public void B2CSalesReturn_InheritsOriginClassification()
    {
        var result = Sut.ClassifySalesReturn(new SalesReturn
        {
            Sale = Sale(GstRegistrationType.Unregistered, gstin: null),
        });

        AssertSale(result, GstTransactionClass.B2C);
    }

    [Fact]
    public void CustomerChanges_DoNotAlterSalesReturnClassification()
    {
        var customer = Customer(GstRegistrationType.Regular);
        var sale = CaptureSale(customer);
        var salesReturn = new SalesReturn { Sale = sale };
        customer.GstRegistrationType = GstRegistrationType.Unregistered;
        customer.Gstin = null;

        AssertSale(Sut.ClassifySalesReturn(salesReturn), GstTransactionClass.B2B);
    }

    [Fact]
    public void PurchaseReturn_InheritsOriginSupplierIdentityAndJurisdiction()
    {
        var purchaseReturn = new PurchaseReturn { Purchase = CapturePurchase(Supplier(GstRegistrationType.Regular)) };

        var result = ContextResolver.ResolvePurchaseReturn(purchaseReturn);

        Assert.Equal(GstPurchasePartyClass.RegisteredSupplier, result.SupplierClassification);
        Assert.Equal(GstJurisdiction.IntraState, result.Jurisdiction);
    }

    [Fact]
    public void UnregisteredSupplier_HasExplicitPurchaseSideIdentityWithoutB2CTerminology()
    {
        var result = Sut.ClassifyPurchase(CapturePurchase(Supplier(GstRegistrationType.Unregistered, gstin: null)));

        Assert.True(result.IsResolved);
        Assert.Equal(GstPurchasePartyClass.UnregisteredSupplier, result.Classification);
    }

    [Fact]
    public void RegisteredSupplierWithMissingGstin_IsUnresolved()
    {
        var result = Sut.ClassifyPurchase(CapturePurchase(Supplier(GstRegistrationType.Regular, gstin: null)));

        Assert.False(result.IsResolved);
        Assert.Equal(GstIdentityClassificationReason.MissingGstin, result.Reason);
    }

    [Fact]
    public void ClassificationDoesNotMutateSaleSnapshotOrCurrentMaster()
    {
        var sale = Sale(GstRegistrationType.Regular);
        sale.Customer = Customer(GstRegistrationType.Unregistered, gstin: null);
        var before = SaleEvidence(sale);
        var currentMasterBefore = (sale.Customer.GstRegistrationType, sale.Customer.Gstin, sale.Customer.StateCode);

        _ = Sut.ClassifySale(sale);

        Assert.Equal(before, SaleEvidence(sale));
        Assert.Equal(currentMasterBefore, (sale.Customer.GstRegistrationType, sale.Customer.Gstin, sale.Customer.StateCode));
    }

    [Fact]
    public void ClassificationDoesNotMutatePurchaseSnapshotOrCurrentMaster()
    {
        var purchase = CapturePurchase(Supplier(GstRegistrationType.Regular));
        var before = PurchaseEvidence(purchase);
        var currentMasterBefore = (
            purchase.Supplier.GstRegistrationType,
            purchase.Supplier.Gstin,
            purchase.Supplier.StateCode);

        _ = Sut.ClassifyPurchase(purchase);

        Assert.Equal(before, PurchaseEvidence(purchase));
        Assert.Equal(currentMasterBefore, (
            purchase.Supplier.GstRegistrationType,
            purchase.Supplier.Gstin,
            purchase.Supplier.StateCode));
    }

    [Fact]
    public void ClassifierHasNoDatabaseOrAuditDependency()
    {
        var constructors = typeof(GstTransactionClassifier).GetConstructors();
        var instanceFields = typeof(GstTransactionClassifier)
            .GetFields(System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic);

        Assert.Contains(constructors, constructor => constructor.GetParameters().Length == 0);
        Assert.Empty(instanceFields);
    }

    private static Sale Sale(
        GstRegistrationType? registrationType,
        string? storeState = "27",
        string? customerState = "27",
        string? gstin = MaharashtraGstin,
        bool customerPresent = true) => new()
        {
            CustomerId = customerPresent ? 42 : null,
            GstIdentitySnapshotCapturedAtUtc = CapturedAt,
            StoreStateCodeSnapshot = storeState,
            CustomerStateCodeSnapshot = customerState,
            CustomerGstinSnapshot = gstin,
            CustomerGstRegistrationTypeSnapshot = registrationType,
        };

    private static Sale CaptureSale(Customer customer, Store? store = null)
    {
        var sale = new Sale { CustomerId = 42, Customer = customer };
        HistoricalGstIdentitySnapshotFactory.Capture(sale, store ?? Store(), customer, CapturedAt);
        return sale;
    }

    private static Purchase CapturePurchase(Supplier supplier, Store? store = null)
    {
        var purchase = new Purchase { SupplierId = 21, Supplier = supplier };
        HistoricalGstIdentitySnapshotFactory.Capture(purchase, store ?? Store(), supplier, CapturedAt);
        return purchase;
    }

    private static Store Store() => new()
    {
        Name = "Historical Store",
        LegalName = "Historical Store Private Limited",
        Gstin = MaharashtraGstin,
        StateCode = "27",
        GstRegistrationType = GstRegistrationType.Regular,
    };

    private static Customer Customer(
        GstRegistrationType? registrationType,
        string? gstin = MaharashtraGstin) => new()
        {
            Name = "Historical Customer",
            Gstin = gstin,
            StateCode = "27",
            GstRegistrationType = registrationType,
        };

    private static Supplier Supplier(
        GstRegistrationType? registrationType,
        string? gstin = MaharashtraGstin) => new()
        {
            Name = "Historical Supplier",
            SupplierCode = "SUP-HISTORICAL",
            Gstin = gstin,
            StateCode = "27",
            GstRegistrationType = registrationType,
        };

    private static object SaleEvidence(Sale sale) => new
    {
        sale.GstIdentitySnapshotCapturedAtUtc,
        sale.CustomerId,
        sale.CustomerGstinSnapshot,
        sale.CustomerStateCodeSnapshot,
        sale.CustomerGstRegistrationTypeSnapshot,
        sale.StoreStateCodeSnapshot,
    };

    private static object PurchaseEvidence(Purchase purchase) => new
    {
        purchase.GstIdentitySnapshotCapturedAtUtc,
        purchase.SupplierId,
        purchase.SupplierGstinSnapshot,
        purchase.SupplierStateCodeSnapshot,
        purchase.SupplierGstRegistrationTypeSnapshot,
        purchase.StoreStateCodeSnapshot,
    };

    private static void AssertSale(
        GstTransactionClassification result,
        GstTransactionClass expected)
    {
        Assert.Equal(expected, result.Classification);
        Assert.Equal(expected != GstTransactionClass.Unresolved, result.IsResolved);
    }
}
