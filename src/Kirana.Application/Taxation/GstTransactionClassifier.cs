using Kirana.Domain.Entities;
using Kirana.Domain.Taxation;

namespace Kirana.Application.Taxation;

/// <summary>
/// Pure historical GST party classifier. RegistrationType is authoritative; GSTIN is supporting
/// identity evidence for registered parties. Names, phone numbers, addresses, state display text,
/// and mutable master records are never classification inputs.
/// </summary>
public sealed class GstTransactionClassifier : IGstTransactionClassifier
{
    public static GstTransactionClassifier Shared { get; } = new();

    public GstTransactionClassification ClassifySale(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);

        if (sale.GstIdentitySnapshotCapturedAtUtc is null)
        {
            return SaleUnresolved(
                GstIdentityClassificationReason.LegacyTransaction,
                sale.CustomerGstRegistrationTypeSnapshot,
                sale.CustomerGstinSnapshot);
        }

        // CustomerId is the existing structural walk-in marker. Phase 18A-2 deliberately captures
        // no invented customer identity for this case, while the capture timestamp proves this is
        // a reviewed new-format transaction rather than an ambiguous legacy row.
        if (sale.CustomerId is null)
        {
            return new(
                GstTransactionClass.B2C,
                GstIdentityClassificationReason.ExplicitWalkInCustomer,
                null,
                false,
                GstHistoricalIdentitySource.TransactionSnapshot);
        }

        return sale.CustomerGstRegistrationTypeSnapshot switch
        {
            GstRegistrationType.Unregistered => new(
                GstTransactionClass.B2C,
                GstIdentityClassificationReason.AuthoritativeRegistrationType,
                GstRegistrationType.Unregistered,
                GstinPresent(sale.CustomerGstinSnapshot),
                GstHistoricalIdentitySource.TransactionSnapshot),

            GstRegistrationType.Regular or GstRegistrationType.Composition =>
                ClassifyRegisteredSale(sale),

            _ => SaleUnresolved(
                GstIdentityClassificationReason.MissingRegistrationType,
                null,
                sale.CustomerGstinSnapshot),
        };
    }

    public GstTransactionClassification ClassifySalesReturn(SalesReturn salesReturn)
    {
        ArgumentNullException.ThrowIfNull(salesReturn);
        return ClassifySale(salesReturn.Sale);
    }

    public GstPurchaseTaxIdentityResolution ClassifyPurchase(Purchase purchase)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        if (purchase.GstIdentitySnapshotCapturedAtUtc is null)
        {
            return PurchaseUnresolved(
                GstIdentityClassificationReason.LegacyTransaction,
                purchase.SupplierGstRegistrationTypeSnapshot,
                purchase.SupplierGstinSnapshot);
        }

        return purchase.SupplierGstRegistrationTypeSnapshot switch
        {
            GstRegistrationType.Unregistered => new(
                GstPurchasePartyClass.UnregisteredSupplier,
                GstIdentityClassificationReason.AuthoritativeRegistrationType,
                GstRegistrationType.Unregistered,
                GstinPresent(purchase.SupplierGstinSnapshot),
                GstHistoricalIdentitySource.TransactionSnapshot),

            GstRegistrationType.Regular or GstRegistrationType.Composition =>
                ClassifyRegisteredPurchase(purchase),

            _ => PurchaseUnresolved(
                GstIdentityClassificationReason.MissingRegistrationType,
                null,
                purchase.SupplierGstinSnapshot),
        };
    }

    public GstPurchaseTaxIdentityResolution ClassifyPurchaseReturn(PurchaseReturn purchaseReturn)
    {
        ArgumentNullException.ThrowIfNull(purchaseReturn);
        return ClassifyPurchase(purchaseReturn.Purchase);
    }

    private static GstTransactionClassification ClassifyRegisteredSale(Sale sale)
    {
        var validation = GstinValidator.ValidateIdentity(
            sale.CustomerGstinSnapshot,
            sale.CustomerStateCodeSnapshot);

        if (validation.Gstin.Status == GstinValidationStatus.Missing)
        {
            return SaleUnresolved(
                GstIdentityClassificationReason.MissingGstin,
                sale.CustomerGstRegistrationTypeSnapshot,
                sale.CustomerGstinSnapshot);
        }

        if (!validation.IsValid || !validation.Gstin.IsValid)
        {
            return SaleUnresolved(
                GstIdentityClassificationReason.InvalidGstin,
                sale.CustomerGstRegistrationTypeSnapshot,
                sale.CustomerGstinSnapshot);
        }

        return new(
            GstTransactionClass.B2B,
            GstIdentityClassificationReason.AuthoritativeRegistrationType,
            sale.CustomerGstRegistrationTypeSnapshot,
            true,
            GstHistoricalIdentitySource.TransactionSnapshot);
    }

    private static GstPurchaseTaxIdentityResolution ClassifyRegisteredPurchase(Purchase purchase)
    {
        var validation = GstinValidator.ValidateIdentity(
            purchase.SupplierGstinSnapshot,
            purchase.SupplierStateCodeSnapshot);

        if (validation.Gstin.Status == GstinValidationStatus.Missing)
        {
            return PurchaseUnresolved(
                GstIdentityClassificationReason.MissingGstin,
                purchase.SupplierGstRegistrationTypeSnapshot,
                purchase.SupplierGstinSnapshot);
        }

        if (!validation.IsValid || !validation.Gstin.IsValid)
        {
            return PurchaseUnresolved(
                GstIdentityClassificationReason.InvalidGstin,
                purchase.SupplierGstRegistrationTypeSnapshot,
                purchase.SupplierGstinSnapshot);
        }

        return new(
            GstPurchasePartyClass.RegisteredSupplier,
            GstIdentityClassificationReason.AuthoritativeRegistrationType,
            purchase.SupplierGstRegistrationTypeSnapshot,
            true,
            GstHistoricalIdentitySource.TransactionSnapshot);
    }

    private static GstTransactionClassification SaleUnresolved(
        GstIdentityClassificationReason reason,
        GstRegistrationType? registrationType,
        string? gstin) =>
        new(
            GstTransactionClass.Unresolved,
            reason,
            registrationType,
            GstinPresent(gstin),
            reason == GstIdentityClassificationReason.LegacyTransaction
                ? GstHistoricalIdentitySource.None
                : GstHistoricalIdentitySource.TransactionSnapshot);

    private static GstPurchaseTaxIdentityResolution PurchaseUnresolved(
        GstIdentityClassificationReason reason,
        GstRegistrationType? registrationType,
        string? gstin) =>
        new(
            GstPurchasePartyClass.Unresolved,
            reason,
            registrationType,
            GstinPresent(gstin),
            reason == GstIdentityClassificationReason.LegacyTransaction
                ? GstHistoricalIdentitySource.None
                : GstHistoricalIdentitySource.TransactionSnapshot);

    private static bool GstinPresent(string? gstin) => !string.IsNullOrWhiteSpace(gstin);
}
