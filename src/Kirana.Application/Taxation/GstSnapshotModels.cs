using Kirana.Domain.Entities;

namespace Kirana.Application.Taxation;

/// <summary>A tax snapshot row read from an already-completed transaction. It is aggregated as-is;
/// historical tax is never recalculated from current product prices or rates.</summary>
public sealed class GstSnapshotLine
{
    public int TransactionId { get; init; }
    public decimal RatePercent { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal GstAmount { get; init; }
    public PricingType PricingType { get; init; } = PricingType.Inclusive;
}

public sealed class GstSlabSummary
{
    public decimal RatePercent { get; init; }
    public decimal TaxableAmount { get; init; }
    public decimal GstAmount { get; init; }
    public int InvoiceCount { get; init; }
    public PricingType PricingType { get; init; } = PricingType.Inclusive;
}

public sealed class GstStoredTotals
{
    public decimal TaxableTotal { get; init; }
    public decimal GstTotal { get; init; }
    public decimal RoundOffAmount { get; init; }
    public decimal GrandTotal { get; init; }
}
