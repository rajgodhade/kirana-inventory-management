using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the supplier search picker opened from Purchase Entry (PRD §28).</summary>
public sealed partial class SupplierPickerViewModel : ObservableObject
{
    private readonly PurchaseEntryViewModel _owner;

    public ObservableCollection<Supplier> Results { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public Supplier? SelectedSupplier { get; private set; }

    public SupplierPickerViewModel(PurchaseEntryViewModel owner)
    {
        _owner = owner;
    }

    public async Task SearchAsync()
    {
        ErrorMessage = null;
        SelectedSupplier = null;

        var results = await _owner.SearchSuppliersAsync(SearchText);
        Results.Clear();
        foreach (var supplier in results)
        {
            Results.Add(supplier);
        }

        if (Results.Count == 0)
        {
            ErrorMessage = "No active suppliers matched that search.";
        }
    }

    public void Pick(Supplier supplier)
    {
        SelectedSupplier = supplier;
        ErrorMessage = null;
    }
}
