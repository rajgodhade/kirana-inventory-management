namespace Kirana.Application.Purchasing;

/// <summary>Read-only control view over PO, completed GRN and actual Purchase records.</summary>
public interface IPurchaseReconciliationService
{
    Task<PurchaseReconciliationResult> SearchAsync(
        PurchaseReconciliationQuery query,
        int? performedByUserId,
        CancellationToken cancellationToken = default);

    Task<PurchaseReconciliationRecord?> GetByPurchaseOrderIdAsync(
        int purchaseOrderId,
        int? performedByUserId,
        CancellationToken cancellationToken = default);
}

