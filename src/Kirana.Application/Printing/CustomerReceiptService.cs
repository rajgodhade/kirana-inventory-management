using Kirana.Application.Abstractions;
using Kirana.Application.Customers;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Printing;

public sealed class CustomerReceiptService(
    IKiranaDbContext db, ICustomerCreditService creditService, IAuditLogger auditLogger) : ICustomerReceiptService
{
    public async Task<CustomerReceiptDocument> GetReceiptAsync(
        int creditPaymentId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        // Permission is enforced inside the credit service; this method deliberately has no gate of
        // its own so there is exactly one place that decides who may see customer financial data.
        var payment = await creditService.GetRepaymentByIdAsync(creditPaymentId, performedByUserId, cancellationToken)
            ?? throw new InvalidOperationException($"Repayment #{creditPaymentId} was not found.");

        var store = await db.Stores.FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("Store is not configured.");

        var receivedBy = payment.RecordedByUserId is { } userId
            ? (await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken))?.FullName
            : null;

        // The balance shown as "before" is reconstructed from the balance now plus everything this
        // receipt settled — never read live, so a reprint years later still shows the same figures.
        var balanceAfter = payment.Customer.CreditBalance;
        var balanceBefore = balanceAfter + payment.Amount;

        var allocations = payment.Allocations
            .OrderBy(a => a.CustomerCredit.DateUtc)
            .Select(a => new CustomerReceiptAllocationLine
            {
                InvoiceNumber = a.CustomerCredit.Sale?.InvoiceNumber ?? $"Sale #{a.CustomerCredit.SaleId}",
                SaleDateUtc = a.CustomerCredit.DateUtc,
                AmountApplied = a.Amount,
                RemainingOnThatCredit = a.CustomerCredit.RemainingAmount,
            })
            .ToList();

        return new CustomerReceiptDocument
        {
            CreditPaymentId = payment.Id,
            StoreName = store.Name,
            StoreAddress = BuildStoreAddress(store),
            StoreContactNumber = store.ContactNumber,
            FooterText = store.InvoiceFooterText,
            ReceiptNumber = payment.ReceiptNumber,
            PaymentDateUtc = payment.PaymentDateUtc,
            ReceivedByName = receivedBy,
            CustomerCode = payment.Customer.CustomerCode,
            CustomerName = payment.Customer.Name,
            CustomerPhone = payment.Customer.Phone,
            AmountPaid = payment.Amount,
            PaymentMethod = payment.Method.ToString(),
            ReferenceNumber = payment.ReferenceNumber,
            BalanceBefore = balanceBefore,
            BalanceAfter = balanceAfter,
            Notes = payment.Notes,
            Allocations = allocations,
        };
    }

    public Task LogPrintAsync(int creditPaymentId, int? userId, CancellationToken cancellationToken = default) =>
        auditLogger.RecordAsync(
            userId, "CreditReceiptPrinted", nameof(CreditPayment), creditPaymentId.ToString(),
            cancellationToken: cancellationToken);

    private static string? BuildStoreAddress(Store store)
    {
        var parts = new[] { store.Address, store.City, store.State, store.PinCode }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        var combined = string.Join(", ", parts);
        return combined.Length == 0 ? null : combined;
    }
}
