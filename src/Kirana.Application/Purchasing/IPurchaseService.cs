using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

/// <summary>
/// Finalizes purchases (PRD §28, §43): validates products/quantities, prices the lines via
/// <see cref="PurchasePricingCalculator"/>, and atomically persists the Purchase, its
/// PurchaseItems (with a full historical snapshot), inventory increase + StockMovements,
/// batch creation/update, and any initial supplier payment — all in one transaction, or none of
/// it. Also supports recording full/partial supplier payments after the fact.
///
/// Every method — reads included — requires <see cref="PermissionKeys.PurchasesManage"/>, since a
/// purchase exposes negotiated purchase prices and outstanding amounts (PRD §6, §9).
/// </summary>
public interface IPurchaseService
{
    Task<Purchase> FinalizePurchaseAsync(CreatePurchaseRequest request, CancellationToken cancellationToken = default);

    Task<SupplierPayment> RecordPaymentAsync(RecordSupplierPaymentRequest request, CancellationToken cancellationToken = default);

    Task<Purchase?> GetByIdAsync(int purchaseId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Purchase>> SearchAsync(PurchaseSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);
}
