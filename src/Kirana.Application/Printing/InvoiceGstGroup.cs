namespace Kirana.Application.Printing;

/// <summary>One row of the printed GST breakdown table — all invoice lines sharing the same
/// snapshotted GST rate, summed (PRD §23). No <c>required</c> members — see the note on
/// <see cref="InvoiceDocument"/> for why.</summary>
public sealed class InvoiceGstGroup
{
    public decimal RatePercent { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal GstAmount { get; init; }
}
