using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;

namespace Kirana.Application.Taxation;

/// <summary>
/// The single capture policy for historical GST/legal identity. It deliberately copies master
/// values without inferring missing facts; state display names are resolved only from the
/// normalized Phase 18A-1 state code catalog.
/// </summary>
public static class HistoricalGstIdentitySnapshotFactory
{
    public static void Capture(Sale sale, Store? store, Customer? customer, DateTime capturedAtUtc)
    {
        sale.GstIdentitySnapshotCapturedAtUtc = capturedAtUtc;
        CaptureStore(store,
            value => sale.StoreTradeNameSnapshot = value,
            value => sale.StoreLegalNameSnapshot = value,
            value => sale.StoreGstinSnapshot = value,
            value => sale.StoreStateCodeSnapshot = value,
            value => sale.StoreStateNameSnapshot = value,
            value => sale.StoreGstRegistrationTypeSnapshot = value,
            value => sale.StoreAddressSnapshot = value,
            value => sale.StoreCitySnapshot = value,
            value => sale.StorePinCodeSnapshot = value,
            value => sale.StoreContactNumberSnapshot = value);

        if (customer is null) return; // Walk-in sale: never invent a customer identity.

        sale.CustomerNameSnapshot = NullIfWhiteSpace(customer.Name);
        sale.CustomerPhoneSnapshot = NullIfWhiteSpace(customer.Phone);
        sale.CustomerGstinSnapshot = NullIfWhiteSpace(customer.Gstin);
        sale.CustomerStateCodeSnapshot = NullIfWhiteSpace(customer.StateCode);
        sale.CustomerStateNameSnapshot = StateName(customer.StateCode);
        sale.CustomerGstRegistrationTypeSnapshot = customer.GstRegistrationType;
        sale.CustomerAddressSnapshot = NullIfWhiteSpace(customer.Address);
    }

    public static void Capture(Purchase purchase, Store? store, Supplier supplier, DateTime capturedAtUtc)
    {
        purchase.GstIdentitySnapshotCapturedAtUtc = capturedAtUtc;
        CaptureStore(store,
            value => purchase.StoreTradeNameSnapshot = value,
            value => purchase.StoreLegalNameSnapshot = value,
            value => purchase.StoreGstinSnapshot = value,
            value => purchase.StoreStateCodeSnapshot = value,
            value => purchase.StoreStateNameSnapshot = value,
            value => purchase.StoreGstRegistrationTypeSnapshot = value,
            value => purchase.StoreAddressSnapshot = value,
            value => purchase.StoreCitySnapshot = value,
            value => purchase.StorePinCodeSnapshot = value,
            value => purchase.StoreContactNumberSnapshot = value);

        purchase.SupplierNameSnapshot = NullIfWhiteSpace(supplier.Name);
        purchase.SupplierCodeSnapshot = NullIfWhiteSpace(supplier.SupplierCode);
        purchase.SupplierGstinSnapshot = NullIfWhiteSpace(supplier.Gstin);
        purchase.SupplierStateCodeSnapshot = NullIfWhiteSpace(supplier.StateCode);
        purchase.SupplierStateNameSnapshot = StateName(supplier.StateCode);
        purchase.SupplierGstRegistrationTypeSnapshot = supplier.GstRegistrationType;
        purchase.SupplierAddressSnapshot = NullIfWhiteSpace(supplier.Address);
    }

    private static void CaptureStore(
        Store? store,
        Action<string?> tradeName,
        Action<string?> legalName,
        Action<string?> gstin,
        Action<string?> stateCode,
        Action<string?> stateName,
        Action<GstRegistrationType?> registrationType,
        Action<string?> address,
        Action<string?> city,
        Action<string?> pinCode,
        Action<string?> contactNumber)
    {
        if (store is null) return;
        tradeName(NullIfWhiteSpace(store.Name));
        legalName(NullIfWhiteSpace(store.LegalName));
        gstin(NullIfWhiteSpace(store.Gstin));
        stateCode(NullIfWhiteSpace(store.StateCode));
        stateName(StateName(store.StateCode) ?? NullIfWhiteSpace(store.State));
        registrationType(store.GstRegistrationType);
        address(NullIfWhiteSpace(store.Address));
        city(NullIfWhiteSpace(store.City));
        pinCode(NullIfWhiteSpace(store.PinCode));
        contactNumber(NullIfWhiteSpace(store.ContactNumber));
    }

    private static string? StateName(string? stateCode) => IndianGstStateCatalog.FindByCode(stateCode)?.Name;

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
