using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Manage Batches dialog (PRD §27) — only relevant when a product opts into
/// batch/expiry tracking.</summary>
public sealed partial class BatchManagementViewModel : ObservableObject
{
    private readonly ProductsViewModel _owner;
    private readonly int _productId;

    public string ProductName { get; }

    public ObservableCollection<ProductBatch> Batches { get; } = [];

    [ObservableProperty]
    private string _batchNumber = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _manufacturingDate;

    [ObservableProperty]
    private DateTimeOffset? _expiryDate;

    [ObservableProperty]
    private string _quantityText = "0";

    [ObservableProperty]
    private string? _purchasePriceText;

    [ObservableProperty]
    private string? _sellingPriceText;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSaving;

    public BatchManagementViewModel(ProductsViewModel owner, Product product)
    {
        _owner = owner;
        _productId = product.Id;
        ProductName = product.Name;
    }

    public async Task LoadAsync()
    {
        var batches = await _owner.GetBatchesAsync(_productId);
        Batches.Clear();
        foreach (var batch in batches)
        {
            Batches.Add(batch);
        }
    }

    [RelayCommand]
    private async Task AddBatchAsync()
    {
        ErrorMessage = null;

        if (string.IsNullOrWhiteSpace(BatchNumber))
        {
            ErrorMessage = "Batch number is required.";
            return;
        }

        if (!decimal.TryParse(QuantityText, out var quantity) || quantity < 0)
        {
            ErrorMessage = "Quantity must be a valid, non-negative number.";
            return;
        }

        if (!TryParseOptionalDecimal(PurchasePriceText, out var purchasePrice))
        {
            ErrorMessage = "Purchase price must be a valid number.";
            return;
        }

        if (!TryParseOptionalDecimal(SellingPriceText, out var sellingPrice))
        {
            ErrorMessage = "Selling price must be a valid number.";
            return;
        }

        IsSaving = true;
        try
        {
            await _owner.AddBatchAsync(
                _productId,
                BatchNumber,
                ManufacturingDate is { } mfg ? DateOnly.FromDateTime(mfg.Date) : null,
                ExpiryDate is { } exp ? DateOnly.FromDateTime(exp.Date) : null,
                quantity,
                purchasePrice,
                sellingPrice);

            BatchNumber = string.Empty;
            QuantityText = "0";
            PurchasePriceText = null;
            SellingPriceText = null;
            ManufacturingDate = null;
            ExpiryDate = null;

            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }

    private static bool TryParseOptionalDecimal(string? text, out decimal? value)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            value = null;
            return true;
        }

        if (decimal.TryParse(text, out var parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }
}
