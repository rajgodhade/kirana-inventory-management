using Kirana.Domain.Entities;

namespace Kirana.Application.Taxation;

/// <summary>
/// Small read-only orchestrator over the independent classification and jurisdiction services.
/// </summary>
public sealed class GstTaxContextResolver(
    IGstTransactionClassifier transactionClassifier,
    IGstJurisdictionResolver jurisdictionResolver) : IGstTaxContextResolver
{
    public static GstTaxContextResolver Shared { get; } =
        new(GstTransactionClassifier.Shared, GstJurisdictionResolver.Shared);

    public GstTaxContext ResolveSale(Sale sale)
    {
        ArgumentNullException.ThrowIfNull(sale);
        return new(transactionClassifier.ClassifySale(sale), jurisdictionResolver.ResolveSale(sale));
    }

    public GstTaxContext ResolveSalesReturn(SalesReturn salesReturn)
    {
        ArgumentNullException.ThrowIfNull(salesReturn);
        return ResolveSale(salesReturn.Sale);
    }

    public GstPurchaseTaxContext ResolvePurchase(Purchase purchase)
    {
        ArgumentNullException.ThrowIfNull(purchase);
        return new(transactionClassifier.ClassifyPurchase(purchase), jurisdictionResolver.ResolvePurchase(purchase));
    }

    public GstPurchaseTaxContext ResolvePurchaseReturn(PurchaseReturn purchaseReturn)
    {
        ArgumentNullException.ThrowIfNull(purchaseReturn);
        return ResolvePurchase(purchaseReturn.Purchase);
    }
}
