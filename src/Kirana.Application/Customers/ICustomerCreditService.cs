using Kirana.Domain.Entities;

namespace Kirana.Application.Customers;

/// <summary>
/// Customer Udhaar: repayments, ledger, purchase history and outstanding balances (PRD §31).
/// Every method — reads included — requires <see cref="PermissionKeys.CustomersManage"/>, because
/// all of it is customer financial data (PRD §6, §9). Creating the credit itself is not here: that
/// happens inside <c>SaleService</c> when a sale is paid with
/// <see cref="PaymentMethod.CustomerCredit"/>, so a credit sale stays one atomic operation.
/// </summary>
public interface ICustomerCreditService
{
    /// <summary>Atomically records a repayment: the CreditPayment, its allocations against
    /// outstanding credits (oldest first), the drawn-down RemainingAmounts and the customer's
    /// balance all commit together or not at all. Throws if the amount exceeds what is owed.</summary>
    Task<CreditPayment> RecordRepaymentAsync(RecordCreditPaymentRequest request, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CustomerLedgerEntry>> GetLedgerAsync(int customerId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Sales made to this customer, newest first — including fully-paid ones, so this is
    /// purchase history rather than only Udhaar history.</summary>
    Task<IReadOnlyList<Sale>> GetPurchaseHistoryAsync(int customerId, int? performedByUserId, int maxResults = 100, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CreditPayment>> GetRepaymentHistoryAsync(int customerId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Every customer who currently owes something, largest first.</summary>
    Task<IReadOnlyList<CustomerOutstandingSummary>> GetOutstandingSummaryAsync(int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Read-only customer rows enriched with the existing sales and repayment history needed by
    /// the management list. Financial access remains permission-gated just like the ledger.
    /// </summary>
    Task<IReadOnlyList<CustomerOverview>> SearchOverviewAsync(CustomerSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>The customer's still-unsettled credits, oldest first — what a repayment will be
    /// applied to.</summary>
    Task<IReadOnlyList<CustomerCredit>> GetOpenCreditsAsync(int customerId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<CreditPayment?> GetRepaymentByIdAsync(int creditPaymentId, int? performedByUserId, CancellationToken cancellationToken = default);
}
