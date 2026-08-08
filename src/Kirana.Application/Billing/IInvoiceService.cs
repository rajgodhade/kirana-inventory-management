namespace Kirana.Application.Billing;

/// <summary>Permission-aware read access to completed sales. Billing remains the only writer;
/// this service exists solely for invoice management, search, and review.</summary>
public interface IInvoiceService
{
    Task<IReadOnlyList<InvoiceListItem>> SearchAsync(InvoiceSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<bool> CanAccessAsync(int saleId, int? performedByUserId, CancellationToken cancellationToken = default);
}
