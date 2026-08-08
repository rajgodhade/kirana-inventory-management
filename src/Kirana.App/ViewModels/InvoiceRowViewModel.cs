namespace Kirana.App.ViewModels;

public sealed class InvoiceRowViewModel
{
    public int SaleId { get; init; }
    public string InvoiceNumber { get; init; } = string.Empty;
    public int? CustomerId { get; init; }
    public string CustomerName { get; init; } = "Walk-in Customer";
    public string CustomerPhoneText { get; init; } = "Walk-in sale";
    public int? CashierUserId { get; init; }
    public string CashierName { get; init; } = "System";
    public DateTime SaleDateUtc { get; init; }
    public decimal TotalItems { get; init; }
    public string PaymentMethodText { get; init; } = string.Empty;
    public string? PromotionText { get; init; }
    public decimal GrandTotal { get; init; }
    public string StatusText { get; init; } = "Completed";

    public string DateTimeText => SaleDateUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt");
    public string TotalItemsText => $"{TotalItems:0.###} item{(TotalItems == 1 ? string.Empty : "s")}";
    public bool HasPromotion => !string.IsNullOrWhiteSpace(PromotionText);
}
