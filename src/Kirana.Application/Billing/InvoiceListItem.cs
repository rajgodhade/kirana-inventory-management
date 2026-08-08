using Kirana.Domain.Entities;

namespace Kirana.Application.Billing;

/// <summary>Flattened completed-sale row for management screens. It contains existing snapshots
/// only, allowing invoice lists to load without exposing mutable sales state.</summary>
public sealed class InvoiceListItem
{
    public int SaleId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public string CustomerName { get; init; } = "Walk-in Customer";
    public int? CustomerId { get; init; }
    public string? CustomerPhone { get; init; }
    public string CashierName { get; init; } = "System";
    public int? CashierUserId { get; init; }
    public DateTime SaleDateUtc { get; init; }
    public decimal TotalItems { get; init; }
    public string PaymentMethodText { get; init; } = "Payment not recorded";
    public string? PromotionText { get; init; }
    public decimal GrandTotal { get; init; }
    public SaleStatus Status { get; init; }
}
