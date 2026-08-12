using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Domain.Entities;

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
    public IReadOnlyList<PricingType> PricingTypes { get; } = Enum.GetValues<PricingType>();

    /// <summary>Optional purchase pack (Phase 13A), copied from the product at cart-add time. Null
    /// unless the product has one configured — <see cref="HasPurchasePack"/> gates the toggle.</summary>
    public UnitOfMeasure? PurchasePackUnit { get; init; }
    public decimal? PurchasePackSize { get; init; }
    public bool HasPurchasePack => PurchasePackUnit is not null;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveQuantity))]
    [NotifyPropertyChangedFor(nameof(ConvertedQuantityPreviewText))]
    private bool _isPackMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveQuantity))]
    [NotifyPropertyChangedFor(nameof(ConvertedQuantityPreviewText))]
    private string _packQuantityText = "1";

    public decimal PackQuantity => decimal.TryParse(PackQuantityText, out var v) ? v : 0;

    /// <summary>The base-unit quantity this line actually adds to stock — what
    /// <see cref="PurchaseEntryViewModel.RecalculateTotals"/> and the submitted
    /// <c>PurchaseLineInput.Quantity</c> must both use, so the live total always matches what gets
    /// submitted regardless of which mode is active.</summary>
    public decimal EffectiveQuantity => IsPackMode && PurchasePackSize is { } size ? PackQuantity * size : Quantity;

    /// <summary>Live "= 120 Piece" preview so a pack-mode entry never silently reinterprets a
    /// quantity — the converted amount is always visible before the purchase is submitted.</summary>
    public string ConvertedQuantityPreviewText => IsPackMode
        ? $"= {EffectiveQuantity:0.###} {Unit}"
        : string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PricingTypeLabel))]
    private PricingType _pricingType = PricingType.Inclusive;

    public string PricingTypeLabel => PricingType == PricingType.Inclusive ? "GST Included" : "GST Extra";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveQuantity))]
    [NotifyPropertyChangedFor(nameof(ConvertedQuantityPreviewText))]
    private string _quantityText = "1";

    [ObservableProperty]
    private string _unitPriceText = "0";

    [ObservableProperty]
    private string _discountPercentText = "0";

    [ObservableProperty]
    private string _batchNumberText = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _expiryDateOffset;

    [ObservableProperty]
    private decimal _taxableAmount;

    [ObservableProperty]
    private decimal _gstAmount;

    [ObservableProperty]
    private decimal _lineTotal;

    public decimal Quantity => decimal.TryParse(QuantityText, out var v) ? v : 0;

    public decimal UnitPrice => decimal.TryParse(UnitPriceText, out var v) ? v : 0;

    public decimal DiscountPercent => decimal.TryParse(DiscountPercentText, out var v) ? v : 0;

    public DateOnly? ExpiryDate => ExpiryDateOffset is { } offset ? DateOnly.FromDateTime(offset.Date) : null;
}
