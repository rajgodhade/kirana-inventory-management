using Kirana.Domain.Entities;

namespace Kirana.Application.Returns;

/// <summary>
/// Sales returns and refunds (PRD §33). Every member is gated by
/// <see cref="PermissionKeys.SalesProcessRefund"/> — returns move both stock and money, so reads
/// are protected as well as writes, the same stance taken for supplier and customer financials.
/// </summary>
public interface ISalesReturnService
{
    /// <summary>Finds candidate sales to return against, by invoice number, barcode, product or
    /// customer.</summary>
    Task<IReadOnlyList<ReturnableSale>> FindReturnableSalesAsync(
        SaleLookupQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>The one sale plus what is still returnable on each line.</summary>
    Task<ReturnableSale?> GetReturnableSaleAsync(
        int saleId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records the return. Stock, batches, stock movements, the customer's balance, the return
    /// records and the audit entry all commit together or not at all.
    /// </summary>
    Task<SalesReturn> ProcessReturnAsync(CreateSalesReturnRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SalesReturn>> SearchAsync(
        SalesReturnSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<SalesReturn?> GetByIdAsync(int salesReturnId, int? performedByUserId, CancellationToken cancellationToken = default);
}
