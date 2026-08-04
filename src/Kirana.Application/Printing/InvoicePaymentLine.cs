using Kirana.Domain.Entities;

namespace Kirana.Application.Printing;

/// <summary>One tender line on a printed invoice — mirrors a <c>Payment</c> row exactly, so a
/// split payment (e.g. part Cash, part UPI) prints as separate lines (PRD §20, §23). No
/// <c>required</c> members — see the note on <see cref="InvoiceDocument"/> for why.</summary>
public sealed class InvoicePaymentLine
{
    public PaymentMethod Method { get; init; }
    public decimal Amount { get; init; }
    public string? ReferenceNumber { get; init; }
    public decimal? AmountTendered { get; init; }
    public decimal? ChangeGiven { get; init; }
}
