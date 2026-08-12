using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Barcodes;
using Kirana.Domain.Barcodes;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs a standalone scan-and-look-up screen that exercises the reusable scanner/lookup
/// pipeline (PRD §17) end-to-end without any cart/billing UI. The same
/// <see cref="IScannerInputBuffer"/> + <see cref="IBarcodeLookupService"/> pairing is exactly
/// what POS scan-to-cart uses.
/// <para>Phase 13B: this screen uses the DIAGNOSTIC lookup, not the POS one, so a retired code or
/// an inactive product reports what it actually is instead of a misleading "not found" — that's the
/// whole reason the diagnostic variant exists.</para>
/// </summary>
public sealed partial class BarcodeScanTestViewModel : ObservableObject
{
    private readonly IBarcodeLookupService _lookupService;

    public IScannerInputBuffer ScannerBuffer { get; }

    [ObservableProperty]
    private string _lastScannedBarcode = "";

    [ObservableProperty]
    private string _statusMessage = "Click the box below, then scan (or type and press Enter).";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FoundProduct))]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    [NotifyPropertyChangedFor(nameof(SymbologyText))]
    [NotifyPropertyChangedFor(nameof(RoleText))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    [NotifyPropertyChangedFor(nameof(BlockedReason))]
    [NotifyPropertyChangedFor(nameof(ShowBlockedWarning))]
    private BarcodeLookupDiagnostic? _result;

    [ObservableProperty]
    private bool _showNotFound;

    public Product? FoundProduct => Result?.Product;
    public bool HasResult => Result is not null;

    public string SymbologyText => Result is null ? "" : Result.Symbology == BarcodeSymbology.Ean13 ? "EAN-13" : "Code 128";
    public string RoleText => Result?.RoleText ?? "";

    /// <summary>The barcode's own status, plus the product's when that's what's blocking — an active
    /// barcode on a discontinued product still won't scan, and "Active" alone would hide that.</summary>
    public string StatusText => Result is null ? ""
        : !Result.IsProductActive ? $"{Result.StatusText} · product inactive"
        : Result.StatusText;

    public string BlockedReason => Result?.BlockedReason ?? "";
    public bool ShowBlockedWarning => Result is { WouldScanAtPos: false };

    public BarcodeScanTestViewModel(IBarcodeLookupService lookupService)
    {
        _lookupService = lookupService;
        ScannerBuffer = new ScannerInputBuffer();
        ScannerBuffer.BarcodeScanned += barcode => _ = OnBarcodeScannedAsync(barcode);
    }

    private async Task OnBarcodeScannedAsync(string barcode)
    {
        LastScannedBarcode = barcode;
        ShowNotFound = false;
        Result = null;

        var diagnostic = await _lookupService.LookupDiagnosticAsync(barcode);
        if (diagnostic is null)
        {
            ShowNotFound = true;
            StatusMessage = $"No product found for barcode '{barcode}'.";
            return;
        }

        Result = diagnostic;
        StatusMessage = diagnostic.WouldScanAtPos
            ? $"Found: {diagnostic.Product.Name}"
            : $"Found: {diagnostic.Product.Name} — but this code will not scan at billing.";
    }
}
