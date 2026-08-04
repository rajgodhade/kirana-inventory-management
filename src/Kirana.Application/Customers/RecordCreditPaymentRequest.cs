using Kirana.Domain.Entities;

namespace Kirana.Application.Customers;

/// <summary>An Udhaar repayment received from a customer (PRD §31). The amount is settled against
/// the customer's outstanding credits oldest-first; it may never exceed what they actually owe.</summary>
public sealed class RecordCreditPaymentRequest
{
    public required int CustomerId { get; init; }
    public required decimal Amount { get; init; }
    public required PaymentMethod Method { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? Notes { get; init; }
    public int? RecordedByUserId { get; init; }
}
