using Kirana.Application.Printing;
using Kirana.Application.Taxation;
using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;

namespace Kirana.Tests.Taxation;

/// <summary>
/// Phase 18A-2 acceptance and fault-injection guards. Each test names the failure mode it protects:
/// mutable masters are inputs at completion only and never become historical read dependencies.
/// </summary>
public sealed class HistoricalGstIdentitySnapshotTests
{
    private static readonly DateTime CapturedAt = new(2026, 8, 20, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void New_sale_captures_store_identity()
    {
        var sale = CaptureSale();

        Assert.Equal(CapturedAt, sale.GstIdentitySnapshotCapturedAtUtc);
        Assert.Equal("Hitu Kirana", sale.StoreTradeNameSnapshot);
        Assert.Equal("Hitu Kirana Private Limited", sale.StoreLegalNameSnapshot);
        Assert.Equal("27ABCDE1234F1Z5", sale.StoreGstinSnapshot);
        Assert.Equal("27", sale.StoreStateCodeSnapshot);
        Assert.Equal("Maharashtra", sale.StoreStateNameSnapshot);
        Assert.Equal(GstRegistrationType.Regular, sale.StoreGstRegistrationTypeSnapshot);
        Assert.Equal("12 Market Road", sale.StoreAddressSnapshot);
    }

    [Fact]
    public void New_sale_captures_customer_identity()
    {
        var sale = CaptureSale();

        Assert.Equal("Onkar Traders", sale.CustomerNameSnapshot);
        Assert.Equal("9876543210", sale.CustomerPhoneSnapshot);
        Assert.Equal("27AAAAA0000A1Z5", sale.CustomerGstinSnapshot);
        Assert.Equal("27", sale.CustomerStateCodeSnapshot);
        Assert.Equal("Maharashtra", sale.CustomerStateNameSnapshot);
        Assert.Equal(GstRegistrationType.Composition, sale.CustomerGstRegistrationTypeSnapshot);
        Assert.Equal("34 Customer Lane", sale.CustomerAddressSnapshot);
    }

    [Fact]
    public void Sale_without_customer_does_not_invent_identity()
    {
        var sale = new Sale();
        HistoricalGstIdentitySnapshotFactory.Capture(sale, Store(), null, CapturedAt);

        Assert.NotNull(sale.GstIdentitySnapshotCapturedAtUtc);
        Assert.Null(sale.CustomerNameSnapshot);
        Assert.Null(sale.CustomerPhoneSnapshot);
        Assert.Null(sale.CustomerGstinSnapshot);
        Assert.Null(sale.CustomerStateCodeSnapshot);
        Assert.Null(sale.CustomerStateNameSnapshot);
        Assert.Null(sale.CustomerGstRegistrationTypeSnapshot);
        Assert.Null(sale.CustomerAddressSnapshot);
    }

    [Fact]
    public void Changing_customer_after_sale_does_not_change_sale_snapshot()
    {
        var customer = Customer();
        var sale = CaptureSale(customer: customer);
        customer.Name = "Changed Customer";
        customer.Phone = "0000000000";

        Assert.Equal("Onkar Traders", sale.CustomerNameSnapshot);
        Assert.Equal("9876543210", sale.CustomerPhoneSnapshot);
    }

    [Fact]
    public void Changing_store_identity_after_sale_does_not_change_sale_snapshot()
    {
        var store = Store();
        var sale = CaptureSale(store: store);
        store.Name = "Changed Store";
        store.LegalName = "Changed Legal Name";

        Assert.Equal("Hitu Kirana", sale.StoreTradeNameSnapshot);
        Assert.Equal("Hitu Kirana Private Limited", sale.StoreLegalNameSnapshot);
    }

    [Fact]
    public void Changing_customer_gstin_does_not_change_historical_sale()
    {
        var customer = Customer();
        var sale = CaptureSale(customer: customer);
        customer.Gstin = "29BBBBB0000B1Z5";

        Assert.Equal("27AAAAA0000A1Z5", sale.CustomerGstinSnapshot);
    }

    [Fact]
    public void Changing_customer_state_code_does_not_change_historical_sale()
    {
        var customer = Customer();
        var sale = CaptureSale(customer: customer);
        customer.StateCode = "29";

        Assert.Equal("27", sale.CustomerStateCodeSnapshot);
        Assert.Equal("Maharashtra", sale.CustomerStateNameSnapshot);
    }

    [Fact]
    public void Changing_customer_registration_type_does_not_change_historical_sale()
    {
        var customer = Customer();
        var sale = CaptureSale(customer: customer);
        customer.GstRegistrationType = GstRegistrationType.Unregistered;

        Assert.Equal(GstRegistrationType.Composition, sale.CustomerGstRegistrationTypeSnapshot);
    }

    [Fact]
    public void Changing_customer_address_does_not_change_historical_sale()
    {
        var customer = Customer();
        var sale = CaptureSale(customer: customer);
        customer.Address = "Changed address";

        Assert.Equal("34 Customer Lane", sale.CustomerAddressSnapshot);
    }

    [Fact]
    public void New_purchase_captures_supplier_identity()
    {
        var purchase = CapturePurchase();

        Assert.Equal("Kumar Supplier", purchase.SupplierNameSnapshot);
        Assert.Equal("SUP-000001", purchase.SupplierCodeSnapshot);
        Assert.Equal("27CCCCC0000C1Z5", purchase.SupplierGstinSnapshot);
        Assert.Equal("27", purchase.SupplierStateCodeSnapshot);
        Assert.Equal("Maharashtra", purchase.SupplierStateNameSnapshot);
        Assert.Equal(GstRegistrationType.Regular, purchase.SupplierGstRegistrationTypeSnapshot);
        Assert.Equal("56 Supplier Street", purchase.SupplierAddressSnapshot);
    }

    [Fact]
    public void New_purchase_captures_store_identity()
    {
        var purchase = CapturePurchase();

        Assert.Equal(CapturedAt, purchase.GstIdentitySnapshotCapturedAtUtc);
        Assert.Equal("Hitu Kirana", purchase.StoreTradeNameSnapshot);
        Assert.Equal("27ABCDE1234F1Z5", purchase.StoreGstinSnapshot);
        Assert.Equal("27", purchase.StoreStateCodeSnapshot);
    }

    [Fact]
    public void Changing_supplier_after_purchase_does_not_alter_historical_purchase()
    {
        var supplier = Supplier();
        var purchase = CapturePurchase(supplier: supplier);
        supplier.Name = "Changed Supplier";
        supplier.SupplierCode = "SUP-CHANGED";

        Assert.Equal("Kumar Supplier", purchase.SupplierNameSnapshot);
        Assert.Equal("SUP-000001", purchase.SupplierCodeSnapshot);
    }

    [Fact]
    public void Changing_supplier_gstin_does_not_alter_historical_purchase()
    {
        var supplier = Supplier();
        var purchase = CapturePurchase(supplier: supplier);
        supplier.Gstin = "29DDDDD0000D1Z5";

        Assert.Equal("27CCCCC0000C1Z5", purchase.SupplierGstinSnapshot);
    }

    [Fact]
    public void Changing_supplier_state_code_does_not_alter_historical_purchase()
    {
        var supplier = Supplier();
        var purchase = CapturePurchase(supplier: supplier);
        supplier.StateCode = "29";

        Assert.Equal("27", purchase.SupplierStateCodeSnapshot);
        Assert.Equal("Maharashtra", purchase.SupplierStateNameSnapshot);
    }

    [Fact]
    public void Changing_supplier_registration_type_does_not_alter_historical_purchase()
    {
        var supplier = Supplier();
        var purchase = CapturePurchase(supplier: supplier);
        supplier.GstRegistrationType = GstRegistrationType.Unregistered;

        Assert.Equal(GstRegistrationType.Regular, purchase.SupplierGstRegistrationTypeSnapshot);
    }

    [Fact]
    public void Historical_invoice_reads_snapshot()
    {
        var sale = CaptureSale();
        sale.InvoiceNumber = "INV-2026-000001";

        var document = new InvoiceDocumentBuilder().Build(sale, Store(name: "Live Store"));

        Assert.True(document.HasHistoricalIdentitySnapshot);
        Assert.Equal("Hitu Kirana", document.StoreName);
        Assert.Equal("Onkar Traders", document.CustomerName);
        Assert.Equal("27AAAAA0000A1Z5", document.CustomerGstin);
    }

    [Fact]
    public void Changing_current_customer_identity_does_not_alter_old_invoice()
    {
        var customer = Customer();
        var sale = CaptureSale(customer: customer);
        sale.InvoiceNumber = "INV-2026-000001";
        customer.Name = "Live Customer";
        customer.Gstin = "29BBBBB0000B1Z5";

        var document = new InvoiceDocumentBuilder().Build(sale, Store());

        Assert.Equal("Onkar Traders", document.CustomerName);
        Assert.Equal("27AAAAA0000A1Z5", document.CustomerGstin);
    }

    [Fact]
    public void Changing_current_store_identity_does_not_alter_old_invoice()
    {
        var sale = CaptureSale();
        sale.InvoiceNumber = "INV-2026-000001";
        var liveStore = Store(name: "Live Store");
        liveStore.Gstin = "29EEEEE0000E1Z5";

        var document = new InvoiceDocumentBuilder().Build(sale, liveStore);

        Assert.Equal("Hitu Kirana", document.StoreName);
        Assert.Equal("27ABCDE1234F1Z5", document.StoreGstin);
    }

    [Fact]
    public void Return_uses_originating_transaction_identity()
    {
        var sale = CaptureSale();
        var salesReturn = new SalesReturn { Sale = sale, SaleId = sale.Id };
        var purchase = CapturePurchase();
        var purchaseReturn = new PurchaseReturn { Purchase = purchase, PurchaseId = purchase.Id };

        Assert.Same(sale, salesReturn.Sale);
        Assert.Equal("27AAAAA0000A1Z5", salesReturn.Sale.CustomerGstinSnapshot);
        Assert.Same(purchase, purchaseReturn.Purchase);
        Assert.Equal("27CCCCC0000C1Z5", purchaseReturn.Purchase.SupplierGstinSnapshot);
    }

    [Fact]
    public void Return_does_not_create_duplicate_identity_snapshot()
    {
        var returnTypes = new[] { typeof(SalesReturn), typeof(PurchaseReturn) };

        Assert.All(returnTypes, type => Assert.DoesNotContain(type.GetProperties(),
            property => property.Name.Contains("GstinSnapshot", StringComparison.Ordinal)
                || property.Name.Contains("StateCodeSnapshot", StringComparison.Ordinal)
                || property.Name == "GstIdentitySnapshotCapturedAtUtc"));
    }

    [Fact]
    public void Existing_transaction_remains_untouched()
    {
        var legacy = new Sale { InvoiceNumber = "LEGACY-1", Customer = Customer() };

        _ = new InvoiceDocumentBuilder().Build(legacy, Store());

        Assert.Null(legacy.GstIdentitySnapshotCapturedAtUtc);
        Assert.Null(legacy.CustomerNameSnapshot);
        Assert.Null(legacy.StoreTradeNameSnapshot);
    }

    [Fact]
    public void Null_snapshot_fields_remain_null()
    {
        var sale = new Sale();
        var purchase = new Purchase();

        Assert.Null(sale.GstIdentitySnapshotCapturedAtUtc);
        Assert.Null(sale.CustomerGstinSnapshot);
        Assert.Null(purchase.GstIdentitySnapshotCapturedAtUtc);
        Assert.Null(purchase.SupplierGstinSnapshot);
    }

    [Fact]
    public void Missing_identity_is_not_guessed_during_capture()
    {
        var sale = new Sale();
        var store = Store();
        store.StateCode = null;
        store.State = null;
        store.Gstin = "27ABCDE1234F1Z5";
        var customer = Customer();
        customer.StateCode = null;
        HistoricalGstIdentitySnapshotFactory.Capture(sale, store, customer, CapturedAt);

        Assert.Null(sale.StoreStateCodeSnapshot);
        Assert.Null(sale.StoreStateNameSnapshot);
        Assert.Null(sale.CustomerStateCodeSnapshot);
        Assert.Null(sale.CustomerStateNameSnapshot);
    }

    private static Sale CaptureSale(Store? store = null, Customer? customer = null)
    {
        var sale = new Sale();
        HistoricalGstIdentitySnapshotFactory.Capture(sale, store ?? Store(), customer ?? Customer(), CapturedAt);
        return sale;
    }

    private static Purchase CapturePurchase(Store? store = null, Supplier? supplier = null)
    {
        var purchase = new Purchase();
        HistoricalGstIdentitySnapshotFactory.Capture(purchase, store ?? Store(), supplier ?? Supplier(), CapturedAt);
        return purchase;
    }

    private static Store Store(string name = "Hitu Kirana") => new()
    {
        Name = name,
        LegalName = "Hitu Kirana Private Limited",
        OwnerName = "Rajendra",
        Gstin = "27ABCDE1234F1Z5",
        StateCode = "27",
        State = "Maharashtra",
        GstRegistrationType = GstRegistrationType.Regular,
        Address = "12 Market Road",
        City = "Pune",
        PinCode = "411001",
        ContactNumber = "9890050022",
    };

    private static Customer Customer() => new()
    {
        Name = "Onkar Traders",
        Phone = "9876543210",
        Gstin = "27AAAAA0000A1Z5",
        StateCode = "27",
        GstRegistrationType = GstRegistrationType.Composition,
        Address = "34 Customer Lane",
    };

    private static Supplier Supplier() => new()
    {
        Name = "Kumar Supplier",
        SupplierCode = "SUP-000001",
        Gstin = "27CCCCC0000C1Z5",
        StateCode = "27",
        GstRegistrationType = GstRegistrationType.Regular,
        Address = "56 Supplier Street",
    };
}
