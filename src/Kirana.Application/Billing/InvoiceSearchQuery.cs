using Kirana.Domain.Entities;

namespace Kirana.Application.Billing;

/// <summary>Read-only filters for the completed-invoice workspace.</summary>
public sealed class InvoiceSearchQuery
{
    public string? SearchText { get; init; }
    public DateTime? FromUtc { get; init; }
    public DateTime? ToUtc { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public int? CashierId { get; init; }
    public int? CustomerId { get; init; }
    public bool? HasPromotion { get; init; }
    public SaleStatus? Status { get; init; }
    public InvoiceSortBy SortBy { get; init; } = InvoiceSortBy.Newest;
    public int MaxResults { get; init; } = 500;
}

public enum InvoiceSortBy { Newest, Oldest, AmountHighToLow, AmountLowToHigh }
