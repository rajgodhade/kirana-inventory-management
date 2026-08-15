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

    /// <summary>The product's actual selling price at the bill's current price level, resolved when
    /// the line was added — the baseline <see cref="UnitPriceText"/> is compared against to detect
    /// an override. Never mutated by editing the price; that only ever changes
    /// <see cref="UnitPriceText"/>.
    ///
    /// <para>Settable since Phase 15B-3 for exactly one reason: switching the bill between Retail
    /// and Wholesale re-resolves every line, which changes what "the product's price" means for
    /// this bill. Nothing else may write it.</para></summary>
    public required decimal OriginalUnitPrice { get; set; }

    /// <summary>
    /// True when the bill's current price level has no configured price for this product — set by
    /// a level switch that could not resolve this line (Phase 15B-3).
    ///
    /// <para>The line keeps showing its previous amount rather than silently becoming another
    /// level's price, and the bill cannot be paid while any line is in this state. Clearing it
    /// means switching back to a level the product does have, or removing the line.</para>
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPriceIssue))]
    private string? _priceIssue;

    public bool HasPriceIssue => !string.IsNullOrEmpty(PriceIssue);

    /// <summary>The product's MRP at the moment this line was added — used for the "You Saved"
    /// figure and shown directly in its own cart column. 0 for a product with no MRP set, which
    /// correctly contributes nothing to the savings total rather than showing a bogus negative
    /// saving, and hides the MRP cell instead of showing "₹0.00".</summary>
    public decimal Mrp { get; init; }

    public bool HasMrp => Mrp > 0;

    public decimal GstRatePercent { get; init; }
    public PricingType PricingType { get; init; } = PricingType.Inclusive;
    public string PricingTypeLabel => PricingType == PricingType.Inclusive ? "GST Included" : "GST Extra";

    [ObservableProperty]
    private string _quantityText = "1";

    [ObservableProperty]
    private string _discountPercentText = "0";

    /// <summary>Editable per-bill price — defaults to <see cref="OriginalUnitPrice"/> but the
    /// cashier can override it for just this sale; the product's own selling price is never
    /// touched. Manager authorization for a changed value is enforced by the code-behind before
    /// the sale is completed, mirroring how a large discount is authorized.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UnitPrice))]
    [NotifyPropertyChangedFor(nameof(IsPriceOverridden))]
    private string _unitPriceText = "0";

    [ObservableProperty]
    private decimal _taxableAmount;

    [ObservableProperty]
    private decimal _gstAmount;

    [ObservableProperty]
    private decimal _lineTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPromotion))]
    private decimal _promotionDiscountAmount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPromotion))]
    private string _promotionText = string.Empty;

    public decimal PromotionBeforeTaxDiscountAmount { get; set; }
    public decimal PromotionAfterTaxDiscountAmount { get; set; }
    public bool HasPromotion => PromotionDiscountAmount > 0 && PromotionText.Length > 0;

    public decimal Quantity => decimal.TryParse(QuantityText, out var v) ? v : 0;

    public decimal DiscountPercent => decimal.TryParse(DiscountPercentText, out var v) ? v : 0;

    public decimal UnitPrice => decimal.TryParse(UnitPriceText, out var v) ? v : 0;

    public bool IsPriceOverridden => UnitPrice != OriginalUnitPrice;
}
