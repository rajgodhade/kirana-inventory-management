using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Customers;

public sealed class CustomerCreditService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
    : ICustomerCreditService
{
    private const string ReceiptSequenceKey = "CreditReceipt";
    private const string ReceiptPrefix = "RCPT";
    private const int ReceiptPadding = 6;

    /// <summary>Repayments are compared against the outstanding balance with a paise-level
    /// tolerance so a customer settling an exact balance is never rejected by a rounding artefact.</summary>
    private const decimal AmountTolerance = 0.01m;

    public async Task<CreditPayment> RecordRepaymentAsync(RecordCreditPaymentRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.RecordedByUserId, PermissionKeys.CustomersManage, cancellationToken);

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Repayment amount must be positive.", nameof(request));
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == request.CustomerId, cancellationToken)
            ?? throw new InvalidOperationException("Customer not found.");

        // Settle oldest debt first, which is what a shopkeeper reconciling a paper ledger does.
        var openCredits = await db.CustomerCredits
            .Include(c => c.Sale)
            .Where(c => c.CustomerId == request.CustomerId && c.RemainingAmount > 0)
            .OrderBy(c => c.DateUtc).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);

        var outstanding = openCredits.Sum(c => c.RemainingAmount);
        if (request.Amount > outstanding + AmountTolerance)
        {
            throw new InvalidOperationException(
                $"Repayment of ₹{request.Amount:0.00} exceeds the outstanding balance of ₹{outstanding:0.00}.");
        }

        var receiptNumber = await sequenceGenerator.NextAsync(ReceiptSequenceKey, ReceiptPrefix, ReceiptPadding, cancellationToken);

        var payment = new CreditPayment
        {
            ReceiptNumber = receiptNumber,
            Customer = customer,
            Amount = request.Amount,
            Method = request.Method,
            ReferenceNumber = request.ReferenceNumber,
            Notes = request.Notes,
            RecordedByUserId = request.RecordedByUserId,
        };

        var remainingToApply = request.Amount;
        foreach (var credit in openCredits)
        {
            if (remainingToApply <= 0)
            {
                break;
            }

            var applied = Math.Min(credit.RemainingAmount, remainingToApply);
            credit.RemainingAmount -= applied;
            credit.UpdatedAtUtc = DateTime.UtcNow;
            remainingToApply -= applied;

            payment.Allocations.Add(new CreditPaymentAllocation
            {
                CreditPayment = payment,
                CustomerCredit = credit,
                Amount = applied,
            });
        }

        // Settling the exact balance can leave a sub-paise sliver from the tolerance above; clamp so
        // the customer isn't left owing a fraction of a paisa forever.
        if (remainingToApply > 0 && openCredits.Count > 0)
        {
            var last = openCredits[^1];
            var allocation = payment.Allocations.LastOrDefault(a => a.CustomerCredit == last);
            if (allocation is not null)
            {
                allocation.Amount += remainingToApply;
            }
        }

        customer.CreditBalance = Math.Max(0m, customer.CreditBalance - request.Amount);
        customer.UpdatedAtUtc = DateTime.UtcNow;

        db.CreditPayments.Add(payment);

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.RecordedByUserId, "CreditRepaymentRecorded", nameof(CreditPayment), payment.Id.ToString(),
            newValue: $"{receiptNumber} — ₹{request.Amount:0.00} from {customer.CustomerCode}",
            reason: $"Settled against {payment.Allocations.Count} credit(s)",
            cancellationToken: cancellationToken);

        return payment;
    }

    public async Task<IReadOnlyList<CustomerLedgerEntry>> GetLedgerAsync(
        int customerId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CustomersManage, cancellationToken);

        var credits = await db.CustomerCredits
            .Include(c => c.Sale)
            .Where(c => c.CustomerId == customerId)
            .OrderBy(c => c.DateUtc)
            .ToListAsync(cancellationToken);

        var payments = await db.CreditPayments
            .Where(p => p.CustomerId == customerId)
            .OrderBy(p => p.PaymentDateUtc)
            .ToListAsync(cancellationToken);

        var events = credits
            .Select(c => (Date: c.DateUtc, IsCredit: true, Credit: (CustomerCredit?)c, Payment: (CreditPayment?)null))
            .Concat(payments.Select(p => (Date: p.PaymentDateUtc, IsCredit: false, Credit: (CustomerCredit?)null, Payment: (CreditPayment?)p)))
            .OrderBy(e => e.Date)
            .ToList();

        var runningBalance = 0m;
        var entries = new List<CustomerLedgerEntry>();
        foreach (var e in events)
        {
            if (e.IsCredit)
            {
                var credit = e.Credit!;
                runningBalance += credit.Amount;
                entries.Add(new CustomerLedgerEntry
                {
                    DateUtc = credit.DateUtc,
                    EntryType = "Credit Sale",
                    Reference = credit.Sale?.InvoiceNumber ?? $"Sale #{credit.SaleId}",
                    DebitAmount = credit.Amount,
                    RunningBalance = runningBalance,
                    Notes = credit.Notes,
                });
            }
            else
            {
                var payment = e.Payment!;
                runningBalance -= payment.Amount;
                entries.Add(new CustomerLedgerEntry
                {
                    DateUtc = payment.PaymentDateUtc,
                    EntryType = "Repayment",
                    Reference = payment.ReceiptNumber,
                    CreditAmount = payment.Amount,
                    RunningBalance = runningBalance,
                    Notes = payment.Notes,
                });
            }
        }

        return entries;
    }

    public async Task<IReadOnlyList<Sale>> GetPurchaseHistoryAsync(
        int customerId, int? performedByUserId, int maxResults = 100, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CustomersManage, cancellationToken);

        return await db.Sales
            .Include(s => s.Payments)
            .Where(s => s.CustomerId == customerId)
            .OrderByDescending(s => s.SaleDateUtc)
            .Take(maxResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CreditPayment>> GetRepaymentHistoryAsync(
        int customerId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CustomersManage, cancellationToken);

        return await db.CreditPayments
            .Include(p => p.Allocations)
            .Where(p => p.CustomerId == customerId)
            .OrderByDescending(p => p.PaymentDateUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerOutstandingSummary>> GetOutstandingSummaryAsync(
        int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CustomersManage, cancellationToken);

        // Derived from the credits themselves rather than Customer.CreditBalance, so the summary is
        // correct even if the denormalized balance were ever to drift.
        var openCredits = await db.CustomerCredits
            .Include(c => c.Customer)
            .Where(c => c.RemainingAmount > 0)
            .ToListAsync(cancellationToken);

        return openCredits
            .GroupBy(c => c.Customer)
            .Select(g => new CustomerOutstandingSummary
            {
                CustomerId = g.Key.Id,
                CustomerCode = g.Key.CustomerCode,
                Name = g.Key.Name,
                Phone = g.Key.Phone,
                OutstandingAmount = g.Sum(c => c.RemainingAmount),
                OpenCreditCount = g.Count(),
                OldestUnpaidDateUtc = g.Min(c => c.DateUtc),
            })
            .OrderByDescending(s => s.OutstandingAmount)
            .ToList();
    }

    public async Task<IReadOnlyList<CustomerCredit>> GetOpenCreditsAsync(
        int customerId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CustomersManage, cancellationToken);

        return await db.CustomerCredits
            .Include(c => c.Sale)
            .Where(c => c.CustomerId == customerId && c.RemainingAmount > 0)
            .OrderBy(c => c.DateUtc).ThenBy(c => c.Id)
            .ToListAsync(cancellationToken);
    }

    public async Task<CreditPayment?> GetRepaymentByIdAsync(
        int creditPaymentId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CustomersManage, cancellationToken);

        return await db.CreditPayments
            .Include(p => p.Customer)
            .Include(p => p.Allocations).ThenInclude(a => a.CustomerCredit).ThenInclude(c => c.Sale)
            .FirstOrDefaultAsync(p => p.Id == creditPaymentId, cancellationToken);
    }
}
