using Kirana.Domain.Entities;

namespace Kirana.Application.Billing;

/// <summary>One tender in a (possibly split) payment (PRD §20).</summary>
public sealed class SalePaymentInput
{
    public required PaymentMethod Method { get; init; }
    public required decimal Amount { get; init; }
    public string? ReferenceNumber { get; init; }

    /// <summary>Cash only — what the customer physically handed over, so change can be computed.</summary>
    public decimal? AmountTendered { get; init; }
}
