using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>One row of the active cart (PRD §18-19). Quantity/discount are user-editable text;
/// the computed amounts are refreshed by <see cref="PosShellViewModel.RecalculateCart"/> whenever
/// anything in the cart changes, so every row always reflects the current bill-level discount too.</summary>
public sealed partial class CartLineViewModel : ObservableObject
{
    public required int ProductId { get; init; }
    public required string ProductName { get; init; }
    public required string ProductCode { get; init; }
    public string? Sku { get; init; }
    public required string Unit { get; init; }
    public required bool SupportsDecimalQuantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public decimal GstRatePercent { get; init; }
    public bool IsTaxInclusive { get; init; }

    [ObservableProperty]
    private string _quantityText = "1";

    [ObservableProperty]
    private string _discountPercentText = "0";

    [ObservableProperty]
    private decimal _taxableAmount;

    [ObservableProperty]
    private decimal _gstAmount;

    [ObservableProperty]
    private decimal _lineTotal;

    public decimal Quantity => decimal.TryParse(QuantityText, out var v) ? v : 0;

    public decimal DiscountPercent => decimal.TryParse(DiscountPercentText, out var v) ? v : 0;
}
