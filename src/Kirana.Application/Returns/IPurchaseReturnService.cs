using Kirana.Domain.Entities;

namespace Kirana.Application.Returns;

/// <summary>
/// Returning goods to a supplier (PRD §34). Gated by <see cref="PermissionKeys.PurchasesManage"/>,
/// the same permission that protects purchases and supplier balances — a purchase return is a
/// credit note against a supplier, so it belongs to exactly that surface.
/// </summary>
public interface IPurchaseReturnService
{
    Task<IReadOnlyList<ReturnablePurchase>> FindReturnablePurchasesAsync(
        string? searchText, int? performedByUserId, int maxResults = 25, CancellationToken cancellationToken = default);

    Task<ReturnablePurchase?> GetReturnablePurchaseAsync(
        int purchaseId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finalizes the return: inventory down, batches down, stock movements written, supplier
    /// outstanding reduced and the return recorded — all in one transaction.
    /// </summary>
    Task<PurchaseReturn> ProcessReturnAsync(CreatePurchaseReturnRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PurchaseReturn>> SearchAsync(
        PurchaseReturnSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<PurchaseReturn?> GetByIdAsync(int purchaseReturnId, int? performedByUserId, CancellationToken cancellationToken = default);
}
