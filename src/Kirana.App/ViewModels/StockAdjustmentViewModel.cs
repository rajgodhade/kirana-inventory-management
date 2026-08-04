using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Adjust Stock dialog (PRD §24-26). Only manual adjustment reasons are
/// offered here — Sale/Purchase/Return movements are written by their own future workflows.</summary>
public sealed partial class StockAdjustmentViewModel : ObservableObject
{
    private readonly ProductsViewModel _owner;
    private readonly int _productId;

    public string ProductName { get; }
    public IReadOnlyList<StockMovementType> AdjustableTypes { get; } =
    [
        StockMovementType.PositiveAdjustment,
        StockMovementType.NegativeAdjustment,
        StockMovementType.Damaged,
        StockMovementType.Expired,
    ];

    [ObservableProperty]
    private decimal _currentStock;

    [ObservableProperty]
    private StockMovementType _selectedMovementType = StockMovementType.PositiveAdjustment;

    [ObservableProperty]
    private string _quantityText = string.Empty;

    [ObservableProperty]
    private string? _reason;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSaving;

    public ObservableCollection<StockMovement> History { get; } = [];

    public event EventHandler? Adjusted;

    public StockAdjustmentViewModel(ProductsViewModel owner, Product product)
    {
        _owner = owner;
        _productId = product.Id;
        ProductName = product.Name;
        CurrentStock = product.Inventory?.QuantityOnHand ?? 0;
    }

    public async Task LoadHistoryAsync()
    {
        var history = await _owner.GetMovementHistoryAsync(_productId);
        History.Clear();
        foreach (var movement in history)
        {
            History.Add(movement);
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        ErrorMessage = null;

        if (!decimal.TryParse(QuantityText, out var magnitude) || magnitude <= 0)
        {
            ErrorMessage = "Enter a quantity greater than zero.";
            return;
        }

        var signedChange = SelectedMovementType.IsIncrease() ? magnitude : -magnitude;

        IsSaving = true;
        try
        {
            await _owner.AdjustStockAsync(_productId, signedChange, SelectedMovementType, Reason);
            CurrentStock += signedChange;
            QuantityText = string.Empty;
            await LoadHistoryAsync();
            Adjusted?.Invoke(this, EventArgs.Empty);
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
}
