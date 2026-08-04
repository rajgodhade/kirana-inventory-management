using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Products;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Manage Brands dialog (PRD §12).</summary>
public sealed partial class BrandManagementViewModel(IBrandService brandService, int? currentUserId) : ObservableObject
{
    [ObservableProperty]
    private string _newBrandName = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<Brand> Brands { get; } = [];

    public async Task LoadAsync()
    {
        var brands = await brandService.GetAllAsync(includeInactive: true);
        Brands.Clear();
        foreach (var brand in brands)
        {
            Brands.Add(brand);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        ErrorMessage = null;
        try
        {
            await brandService.CreateAsync(NewBrandName, currentUserId);
            NewBrandName = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task ToggleActiveAsync(Brand brand)
    {
        await brandService.SetActiveAsync(brand.Id, !brand.IsActive, currentUserId);
        await LoadAsync();
    }
}
