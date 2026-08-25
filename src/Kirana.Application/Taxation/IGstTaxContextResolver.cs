using Kirana.Domain.Entities;

namespace Kirana.Application.Taxation;

/// <summary>Composes classification and jurisdiction without duplicating either decision policy.</summary>
public interface IGstTaxContextResolver
{
    GstTaxContext ResolveSale(Sale sale);
    GstTaxContext ResolveSalesReturn(SalesReturn salesReturn);
    GstPurchaseTaxContext ResolvePurchase(Purchase purchase);
    GstPurchaseTaxContext ResolvePurchaseReturn(PurchaseReturn purchaseReturn);
}
