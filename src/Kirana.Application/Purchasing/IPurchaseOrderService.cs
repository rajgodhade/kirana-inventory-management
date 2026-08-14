using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

/// <summary>Non-posting purchase-order lifecycle. All methods reuse PurchasesManage and must never
/// write Inventory, StockMovement, Purchase, SupplierPayment, or supplier balances.</summary>
public interface IPurchaseOrderService
{
    Task<PurchaseOrder> CreateDraftAsync(SavePurchaseOrderDraftRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseOrder> UpdateDraftAsync(int purchaseOrderId, SavePurchaseOrderDraftRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseOrder> SubmitAsync(int purchaseOrderId, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<PurchaseOrder> CancelAsync(CancelPurchaseOrderRequest request, CancellationToken cancellationToken = default);
    Task<PurchaseOrder?> GetByIdAsync(int purchaseOrderId, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PurchaseOrder>> SearchAsync(PurchaseOrderSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);
}
