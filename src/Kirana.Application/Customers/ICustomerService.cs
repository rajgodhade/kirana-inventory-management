using Kirana.Domain.Entities;

namespace Kirana.Application.Customers;

/// <summary>
/// Customer master data (PRD §30). Deliberately NOT permission-gated: the POS must be able to look
/// up and add a customer while running logged-out in Billing Mode (PRD §4). Customer
/// <em>financial</em> surfaces — ledger, outstanding balances, repayments — live on
/// <see cref="ICustomerCreditService"/> and are gated by
/// <see cref="PermissionKeys.CustomersManage"/>.
/// </summary>
public interface ICustomerService
{
    Task<Customer> CreateAsync(CreateCustomerRequest request, CancellationToken cancellationToken = default);

    Task<Customer> UpdateAsync(int customerId, UpdateCustomerRequest request, CancellationToken cancellationToken = default);

    Task<Customer> SetActiveAsync(int customerId, bool isActive, int? performedByUserId = null, CancellationToken cancellationToken = default);

    Task<Customer?> GetByIdAsync(int customerId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Customer>> SearchAsync(CustomerSearchQuery query, CancellationToken cancellationToken = default);
}
