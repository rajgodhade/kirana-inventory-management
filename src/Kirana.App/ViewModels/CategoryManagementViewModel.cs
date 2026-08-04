using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Products;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Manage Categories dialog (PRD §12).</summary>
public sealed partial class CategoryManagementViewModel(ICategoryService categoryService, int? currentUserId) : ObservableObject
{
    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    public ObservableCollection<Category> Categories { get; } = [];

    public async Task LoadAsync()
    {
        var categories = await categoryService.GetAllAsync(includeInactive: true);
        Categories.Clear();
        foreach (var category in categories)
        {
            Categories.Add(category);
        }
    }

    [RelayCommand]
    private async Task AddAsync()
    {
        ErrorMessage = null;
        try
        {
            await categoryService.CreateAsync(NewCategoryName, currentUserId);
            NewCategoryName = string.Empty;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task ToggleActiveAsync(Category category)
    {
        await categoryService.SetActiveAsync(category.Id, !category.IsActive, currentUserId);
        await LoadAsync();
    }
}
