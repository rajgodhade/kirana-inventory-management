using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>
/// One row in the Add/Edit Product dialog's barcode list (Phase 13B). Carries its own
/// <see cref="Id"/> so edit-mode actions can call <c>IBarcodeService</c> directly; in create mode
/// the product doesn't exist yet, so <see cref="Id"/> stays 0 and the row lives only in memory
/// until Save ships the whole list in the create request.
/// </summary>
public sealed partial class ProductBarcodeRowViewModel : ObservableObject
{
    public int Id { get; init; }

    [ObservableProperty]
    private string _value = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SymbologyLabel))]
    private BarcodeSymbology _symbology = BarcodeSymbology.Code128;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleLabel))]
    [NotifyPropertyChangedFor(nameof(PrimaryGlyph))]
    [NotifyPropertyChangedFor(nameof(CanSetPrimary))]
    private bool _isPrimary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(ActivateToggleLabel))]
    [NotifyPropertyChangedFor(nameof(CanSetPrimary))]
    private bool _isActive = true;

    public string SymbologyLabel => Symbology == BarcodeSymbology.Ean13 ? "EAN-13" : "CODE128";
    public string RoleLabel => IsPrimary ? "Primary" : "Alternate";
    public string StatusLabel => IsActive ? "Active" : "Retired";

    /// <summary>Filled star for the primary, hollow for the rest. Written as escapes rather than
    /// literal glyphs: these are Segoe MDL2 Assets Private Use Area characters (FavoriteStarFill /
    /// FavoriteStar) that do not survive a round trip through every editor and encoding, and a
    /// silently emptied string renders as a blank column with no error to notice.</summary>
    public string PrimaryGlyph => IsPrimary ? "" : "";

    /// <summary>A retired code can't be what label printing defaults to, so it can't be promoted
    /// until it's reactivated.</summary>
    public bool CanSetPrimary => !IsPrimary && IsActive;

    public string ActivateToggleLabel => IsActive ? "Retire" : "Restore";

    public static ProductBarcodeRowViewModel From(ProductBarcode barcode) => new()
    {
        Id = barcode.Id,
        Value = barcode.Value,
        Symbology = barcode.Symbology,
        IsPrimary = barcode.IsPrimary,
        IsActive = barcode.IsActive,
    };
}
