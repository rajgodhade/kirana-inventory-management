using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Expenses;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed class ExpenseRowViewModel
{
    public int Id { get; set; }
    public string ExpenseNumber { get; set; } = string.Empty;
    public string DateText { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string AmountText { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

public sealed partial class ExpenseCategoryRowViewModel : ObservableObject
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    public bool IsSystemDefault { get; set; }

    /// <summary>Built-in headings can be renamed or deactivated but never deleted, so a store that
    /// upgraded looks the same as a fresh install.</summary>
    public bool CanDelete => !IsSystemDefault;
}

/// <summary>Backs the Expenses list: filters, rows, and totals for the current filter.</summary>
public sealed partial class ExpensesViewModel(
    IExpenseService expenseService, IExpenseCategoryService categoryService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalDisplay))]
    private decimal _filteredTotal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountDisplay))]
    private int _filteredCount;

    [ObservableProperty]
    private string _selectedCategoryName = AllCategories;

    public const string AllCategories = "All categories";

    public bool CanManageExpenses => session.HasPermission(PermissionKeys.ExpensesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<ExpenseRowViewModel> Expenses { get; } = [];

    public ObservableCollection<string> CategoryNames { get; } = [AllCategories];

    public ObservableCollection<ExpenseCategory> Categories { get; } = [];

    public string TotalDisplay => SalesReturnsViewModel.FormatCurrency(FilteredTotal);

    public string CountDisplay => FilteredCount == 1 ? "1 expense" : $"{FilteredCount} expenses";

    public async Task InitializeAsync()
    {
        await LoadCategoriesAsync();
        await SearchAsync();
    }

    public async Task LoadCategoriesAsync()
    {
        try
        {
            var categories = await categoryService.GetAllAsync(includeInactive: false, CurrentUserId);

            Categories.Clear();
            CategoryNames.Clear();
            CategoryNames.Add(AllCategories);

            foreach (var category in categories)
            {
                Categories.Add(category);
                CategoryNames.Add(category.Name);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }

    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var query = BuildQuery();

            var results = await expenseService.SearchAsync(query, CurrentUserId);
            Expenses.Clear();
            foreach (var e in results)
            {
                Expenses.Add(new ExpenseRowViewModel
                {
                    Id = e.Id,
                    ExpenseNumber = e.ExpenseNumber,
                    DateText = e.ExpenseDateUtc.ToLocalTime().ToString("dd-MMM-yyyy"),
                    CategoryName = e.CategoryNameSnapshot,
                    AmountText = SalesReturnsViewModel.FormatCurrency(e.Amount),
                    PaymentMethod = e.PaymentMethod.ToString(),
                    Description = e.Description ?? string.Empty,
                });
            }

            // Totals come from the service over the whole filtered set, not this page of rows.
            var totals = await expenseService.GetTotalsAsync(query, CurrentUserId);
            FilteredTotal = totals.TotalAmount;
            FilteredCount = totals.Count;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private ExpenseSearchQuery BuildQuery()
    {
        var categoryId = Categories.FirstOrDefault(c => c.Name == SelectedCategoryName)?.Id;
        return new ExpenseSearchQuery
        {
            SearchText = SearchText,
            ExpenseCategoryId = SelectedCategoryName == AllCategories ? null : categoryId,
        };
    }

    public Task<Expense?> GetExpenseAsync(int id) => expenseService.GetByIdAsync(id, CurrentUserId);

    public Task<Expense> CreateAsync(CreateExpenseRequest request) => expenseService.CreateAsync(request);

    public Task<Expense> UpdateAsync(int id, UpdateExpenseRequest request) => expenseService.UpdateAsync(id, request);

    public Task DeleteAsync(int id) => expenseService.DeleteAsync(id, CurrentUserId);
}

/// <summary>Backs the Expense Categories screen.</summary>
public sealed partial class ExpenseCategoriesViewModel(IExpenseCategoryService categoryService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public bool CanManageExpenses => session.HasPermission(PermissionKeys.ExpensesManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<ExpenseCategoryRowViewModel> Categories { get; } = [];

    public async Task InitializeAsync() => await LoadAsync();

    public async Task LoadAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var categories = await categoryService.GetAllAsync(ShowInactive, CurrentUserId);

            Categories.Clear();
            foreach (var c in categories)
            {
                Categories.Add(new ExpenseCategoryRowViewModel
                {
                    Id = c.Id,
                    Name = c.Name,
                    Description = c.Description ?? string.Empty,
                    IsActive = c.IsActive,
                    IsSystemDefault = c.IsSystemDefault,
                });
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<ExpenseCategory> CreateAsync(string name, string? description) =>
        categoryService.CreateAsync(new CreateExpenseCategoryRequest
        {
            Name = name, Description = description, PerformedByUserId = CurrentUserId,
        });

    public Task<ExpenseCategory> UpdateAsync(int id, string name, string? description) =>
        categoryService.UpdateAsync(id, new UpdateExpenseCategoryRequest
        {
            Name = name, Description = description, PerformedByUserId = CurrentUserId,
        });

    public Task<ExpenseCategory> SetActiveAsync(int id, bool isActive) =>
        categoryService.SetActiveAsync(id, isActive, CurrentUserId);

    public Task DeleteAsync(int id) => categoryService.DeleteAsync(id, CurrentUserId);
}
