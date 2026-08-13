using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.App.Printing;
using Kirana.Application.Abstractions;
using Kirana.Application.Barcodes;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Kirana.App.ViewModels;

/// <summary>Backs the barcode label preview/print dialog (PRD §16) for both a single product and
/// a bulk selection — the only difference is how many <see cref="BarcodeLabelLineItem"/>s the
/// caller seeds it with.</summary>
public sealed partial class BarcodeLabelViewModel : ObservableObject
{
    private readonly ProductsViewModel _owner;
    private readonly IBarcodeService _barcodeService;
    private readonly IBarcodeRenderer _barcodeRenderer;

    public ObservableCollection<BarcodeLabelLineItem> Items { get; } = [];

    [ObservableProperty]
    private string _labelWidthMmText = "40";

    [ObservableProperty]
    private string _labelHeightMmText = "25";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isPrinting;

    public BarcodeLabelViewModel(
        ProductsViewModel owner, IBarcodeService barcodeService, IBarcodeRenderer barcodeRenderer,
        IEnumerable<ProductRowViewModel> products)
    {
        _owner = owner;
        _barcodeService = barcodeService;
        _barcodeRenderer = barcodeRenderer;

        foreach (var product in products)
        {
            var item = new BarcodeLabelLineItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                ProductCode = product.ProductCode,
                Sku = product.Sku,
                Mrp = product.Mrp ?? 0,
                SellingPrice = product.SellingPrice,
            };

            foreach (var option in product.Barcodes)
            {
                item.AvailableBarcodes.Add(option);
            }

            // Default to the primary, so a bulk run over hundreds of products prints the expected
            // code without the operator touching a single picker.
            item.SelectedBarcode = item.AvailableBarcodes.FirstOrDefault(b => b.IsPrimary)
                ?? item.AvailableBarcodes.FirstOrDefault();

            // Re-render whenever the operator picks a different code, so the on-screen preview and
            // the printed label can never disagree.
            item.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(BarcodeLabelLineItem.SelectedBarcode))
                {
                    RenderPreview(item);
                }
            };

            Items.Add(item);
            RenderPreview(item);
        }
    }

    [RelayCommand]
    private async Task GenerateAsync(BarcodeLabelLineItem item)
    {
        ErrorMessage = null;
        item.IsGenerating = true;
        try
        {
            var created = await _barcodeService.AssignBarcodeAsync(item.ProductId, explicitBarcode: null, _owner.CurrentUserId);

            // AssignBarcodeAsync makes the new code primary, so demote the local copies to match
            // rather than re-querying just to refresh a picker.
            foreach (var existing in item.AvailableBarcodes.ToList())
            {
                item.AvailableBarcodes[item.AvailableBarcodes.IndexOf(existing)] = new ProductBarcodeOption
                {
                    Id = existing.Id,
                    Value = existing.Value,
                    Symbology = existing.Symbology,
                    IsPrimary = false,
                    IsActive = existing.IsActive,
                };
            }

            var option = ProductBarcodeOption.From(created);
            item.AvailableBarcodes.Add(option);
            item.SelectedBarcode = option;
            RenderPreview(item);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            item.IsGenerating = false;
        }
    }

    public void RemoveItem(BarcodeLabelLineItem item) => Items.Remove(item);

    private void RenderPreview(BarcodeLabelLineItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Barcode))
        {
            item.BarcodeImage = null;
            return;
        }

        var symbology = _barcodeService.DetermineSymbology(item.Barcode);
        var rendered = _barcodeRenderer.Render(item.Barcode, symbology, 240, 80);
        item.BarcodeImage = ToWriteableBitmap(rendered);
    }

    [RelayCommand]
    private async Task PrintAsync()
    {
        ErrorMessage = null;

        if (Items.Count == 0)
        {
            ErrorMessage = "Add at least one product.";
            return;
        }

        if (!double.TryParse(LabelWidthMmText, out var widthMm) || widthMm <= 0 ||
            !double.TryParse(LabelHeightMmText, out var heightMm) || heightMm <= 0)
        {
            ErrorMessage = "Label width/height must be positive numbers.";
            return;
        }

        if (Items.Any(i => string.IsNullOrWhiteSpace(i.Barcode)))
        {
            ErrorMessage = "Generate or enter a barcode for every label before printing.";
            return;
        }

        var withQuantities = new List<(LabelPrintItem Item, int Quantity)>();
        foreach (var item in Items)
        {
            if (!int.TryParse(item.QuantityText, out var quantity) || quantity <= 0)
            {
                ErrorMessage = $"Quantity for '{item.ProductName}' must be a positive whole number.";
                return;
            }

            withQuantities.Add((new LabelPrintItem
            {
                ProductName = item.ProductName,
                CodeOrSku = item.CodeOrSku,
                BarcodeValue = item.Barcode!,
                Mrp = item.Mrp,
                SellingPrice = item.SellingPrice,
                BarcodeImage = item.BarcodeImage!,
            }, quantity));
        }

        var expanded = LabelLayoutCalculator.ExpandByQuantity(withQuantities);

        IsPrinting = true;
        try
        {
            using var printHelper = new BarcodeLabelPrintHelper(App.MainWindow, expanded, widthMm, heightMm);
            await printHelper.ShowPrintUIAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsPrinting = false;
        }
    }

    private static WriteableBitmap ToWriteableBitmap(BarcodeRenderResult result)
    {
        var bitmap = new WriteableBitmap(result.PixelWidth, result.PixelHeight);
        using (var stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(result.Bgra32Pixels, 0, result.Bgra32Pixels.Length);
        }

        bitmap.Invalidate();
        return bitmap;
    }
}
