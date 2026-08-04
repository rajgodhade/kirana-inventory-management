using Kirana.Domain.Entities;

namespace Kirana.Application.Customers;

/// <summary>Minimal customer directory for POS selection (PRD §30). Ledger/purchase-history
/// management is Phase 8.</summary>
public interface ICustomerService
{
    Task<Customer> CreateAsync(string name, string? phone, string? address, string? gstin, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Customer>> SearchAsync(string? searchText, int maxResults = 50, CancellationToken cancellationToken = default);

    Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken = default);
}
