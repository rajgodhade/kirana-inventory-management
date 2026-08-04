using Kirana.Domain.Entities;

namespace Kirana.Application.Purchasing;

/// <summary>A payment made to a supplier after purchase creation (PRD §28-29) — full or partial,
/// optionally earmarked against one specific purchase.</summary>
public sealed class RecordSupplierPaymentRequest
{
    public required int SupplierId { get; init; }
    public int? PurchaseId { get; init; }
    public required decimal Amount { get; init; }
    public required PaymentMethod Method { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? Notes { get; init; }
    public int? RecordedByUserId { get; init; }
}
