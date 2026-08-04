using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Barcodes;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs a standalone scan-and-look-up screen that exercises the reusable scanner/lookup
/// pipeline (PRD §17) end-to-end without any cart/billing UI — that lands in Phase 4. The same
/// <see cref="IScannerInputBuffer"/> + <see cref="IBarcodeLookupService"/> pairing is exactly
/// what the future POS scan-to-cart flow will reuse unmodified.
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
    private Product? _foundProduct;

    [ObservableProperty]
    private bool _showNotFound;

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
        FoundProduct = null;

        var product = await _lookupService.LookupAsync(barcode);
        if (product is null)
        {
            ShowNotFound = true;
            StatusMessage = $"No product found for barcode '{barcode}'.";
            return;
        }

        FoundProduct = product;
        StatusMessage = $"Found: {product.Name}";
    }
}
