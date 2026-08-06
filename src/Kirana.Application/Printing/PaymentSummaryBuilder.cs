using Kirana.Domain.Entities;

namespace Kirana.Application.Printing;

/// <summary>
/// Decides exactly which rows belong in a receipt's Payment Summary, built purely from the Sale's
/// own immutable <see cref="Payment"/> rows (mapped onto <see cref="InvoicePaymentLine"/> by
/// <see cref="InvoiceDocumentBuilder"/>) — never inferred from the Sale's totals or any live UI
/// state. A "Customer Credit" row is exactly the Payment row already written with
/// <c>Method == PaymentMethod.CustomerCredit</c> at sale time (see <c>SaleService.CompleteSaleAsync</c>);
/// there is nothing further to read from the <see cref="CustomerCredit"/> ledger table itself, since
/// that table exists for tracking repayment against the customer's balance, not for describing what
/// was charged on this one receipt.
/// </summary>
public static class PaymentSummaryBuilder
{
    public static IReadOnlyList<InvoicePaymentSummaryLine> Build(IReadOnlyList<InvoicePaymentLine> payments)
    {
        var rows = new List<InvoicePaymentSummaryLine>();

        foreach (var payment in payments)
        {
            // Zero-value payment rows are never rendered — a defensive guard, since SaleService
            // already requires every payment's Amount to be positive before a sale can complete.
            if (payment.Amount <= 0)
            {
                continue;
            }

            rows.Add(new InvoicePaymentSummaryLine { Label = BuildMethodLabel(payment), Amount = payment.Amount });

            // AmountTendered/ChangeGiven are only ever populated for Cash (SaleService clears both
            // for every other method) — checking the method explicitly here as well, rather than
            // relying on that always being true, is what keeps "no Cash Received/Change Returned for
            // UPI or Card" an actual rule of this builder instead of an accidental side effect.
            if (payment.Method != PaymentMethod.Cash)
            {
                continue;
            }

            // "Cash Received" only earns its own row when it actually differs from the cash line's
            // own amount — otherwise it's the exact same figure printed a second time for no reason,
            // which is what made a plain full-cash sale look no different from a split one.
            if (payment.AmountTendered is { } tendered && tendered != payment.Amount)
            {
                rows.Add(new InvoicePaymentSummaryLine { Label = "Cash Received", Amount = tendered, IsDetail = true });
            }

            // Only a genuine positive change is worth a row. A split sale where the cash portion
            // was tendered exactly (or covered by a separate method) must never show a change
            // amount that was never actually returned to the customer.
            if (payment.ChangeGiven is { } change && change > 0)
            {
                rows.Add(new InvoicePaymentSummaryLine { Label = "Change Returned", Amount = change, IsDetail = true });
            }
        }

        return rows;
    }

    private static string BuildMethodLabel(InvoicePaymentLine payment)
    {
        var name = payment.Method switch
        {
            PaymentMethod.Cash => "Cash Paid",
            PaymentMethod.Upi => "UPI",
            PaymentMethod.Card => "Card",
            PaymentMethod.CustomerCredit => "Customer Credit",
            _ => payment.Method.ToString(),
        };

        return string.IsNullOrWhiteSpace(payment.ReferenceNumber) ? name : $"{name} (Ref: {payment.ReferenceNumber})";
    }
}
