using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

public interface IGoodsReceiptService
{
    Task<PurchaseOrderReceiptPreview> GetReceiptPreviewAsync(int purchaseOrderId, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<GoodsReceipt> CreateDraftAsync(CreateGoodsReceiptDraftRequest request, CancellationToken cancellationToken = default);
    Task<GoodsReceipt> CompleteAsync(int goodsReceiptId, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<GoodsReceipt> CancelAsync(CancelGoodsReceiptRequest request, CancellationToken cancellationToken = default);
    Task<GoodsReceipt?> GetByIdAsync(int goodsReceiptId, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GoodsReceipt>> SearchAsync(GoodsReceiptSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);
    Task<GoodsReceiptPurchasePrefill> GetPurchasePrefillAsync(int goodsReceiptId, int? performedByUserId, CancellationToken cancellationToken = default);
}
