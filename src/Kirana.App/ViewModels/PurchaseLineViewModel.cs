using CommunityToolkit.Mvvm.ComponentModel;

namespace Kirana.App.ViewModels;

/// <summary>One row of a purchase being entered (PRD §28). Quantity/price/discount/batch are
/// user-editable text; computed amounts are refreshed by
/// <see cref="PurchaseEntryViewModel.RecalculateTotals"/> whenever anything changes.</summary>
public sealed partial class PurchaseLineViewModel : ObservableObject
{
    public required int ProductId { get; init; }
    public required string ProductName { get; init; }
    public required string ProductCode { get; init; }
    public string? Sku { get; init; }
    public required string Unit { get; init; }
    public required bool SupportsDecimalQuantity { get; init; }
    public required bool TracksBatches { get; init; }
    public decimal GstRatePercent { get; init; }
    public bool IsTaxInclusive { get; init; }

    [ObservableProperty]
    private string _quantityText = "1";

    [ObservableProperty]
    private string _unitPriceText = "0";

    [ObservableProperty]
    private string _discountPercentText = "0";

    [ObservableProperty]
    private string _batchNumberText = string.Empty;

    [ObservableProperty]
    private string _expiryDateText = string.Empty;

    [ObservableProperty]
    private decimal _taxableAmount;

    [ObservableProperty]
    private decimal _gstAmount;

    [ObservableProperty]
    private decimal _lineTotal;

    public decimal Quantity => decimal.TryParse(QuantityText, out var v) ? v : 0;

    public decimal UnitPrice => decimal.TryParse(UnitPriceText, out var v) ? v : 0;

    public decimal DiscountPercent => decimal.TryParse(DiscountPercentText, out var v) ? v : 0;

    public DateOnly? ExpiryDate => DateOnly.TryParse(ExpiryDateText, out var d) ? d : null;
}
