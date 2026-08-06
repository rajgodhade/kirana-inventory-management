namespace Kirana.Application.Printing;

/// <summary>
/// One already-decided row for the receipt's "Payment Summary" — <see cref="PaymentSummaryBuilder"/>
/// is the only place that decides which rows apply to a given payment scenario (cash-only, split,
/// full credit, etc.); the renderer just displays whatever rows it's handed, with no business logic
/// of its own. No <c>required</c> members — see the note on <see cref="InvoiceDocument"/> for why.
/// </summary>
public sealed class InvoicePaymentSummaryLine
{
    public string Label { get; init; } = string.Empty;
    public decimal Amount { get; init; }

    /// <summary>True for a sub-row ("Cash Received", "Change Returned") that belongs to the payment
    /// line above it — lets the renderer indent it the same way it always has, without needing to
    /// know why.</summary>
    public bool IsDetail { get; init; }
}
