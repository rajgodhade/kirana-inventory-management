using Kirana.Domain.Entities;

namespace Kirana.Application.Taxation;

/// <summary>
/// Classifies GST party identity exclusively from immutable transaction snapshots. Implementations
/// must be pure and must never query or mutate current customer, supplier, or store masters.
/// </summary>
public interface IGstTransactionClassifier
{
    GstTransactionClassification ClassifySale(Sale sale);
    GstTransactionClassification ClassifySalesReturn(SalesReturn salesReturn);
    GstPurchaseTaxIdentityResolution ClassifyPurchase(Purchase purchase);
    GstPurchaseTaxIdentityResolution ClassifyPurchaseReturn(PurchaseReturn purchaseReturn);
}
