using Kirana.App.Printing;
using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Expenses;
using Kirana.Application.Printing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class ExpensesPage : Page
{
    private readonly DispatcherTimer _searchDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public ExpensesViewModel ViewModel { get; }

    public ExpensesPage()
    {
        var services = App.Services;
        ViewModel = new ExpensesViewModel(
            services.GetRequiredService<IExpenseService>(),
            services.GetRequiredService<IExpenseCategoryService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();

        _searchDebounce.Tick += async (_, _) =>
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        };
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _searchDebounce.Stop();
        _searchDebounce.Start();
    }

    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            _searchDebounce.Stop();
            await ViewModel.SearchAsync();
        }
    }

    private async void OnSearchClick(object sender, RoutedEventArgs e)
    {
        _searchDebounce.Stop();
        await ViewModel.SearchAsync();
    }

    private async void OnCategoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsLoaded)
        {
            await ViewModel.SearchAsync();
        }
    }

    private void OnCategoriesClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ExpenseCategoriesPage));

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ExpenseEditDialog(ViewModel, existing: null).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExpenseRowViewModel row)
        {
            return;
        }

        var expense = await ViewModel.GetExpenseAsync(row.Id);
        if (expense is null)
        {
            return;
        }

        var dialog = new ExpenseEditDialog(ViewModel, expense).Themed(XamlRoot);
        await dialog.ShowAsync();
        await ViewModel.SearchAsync();
    }

    private void OnDetailsClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is ExpenseRowViewModel row)
        {
            Frame.Navigate(typeof(ExpenseDetailsPage), row.Id);
        }
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExpenseRowViewModel row)
        {
            return;
        }

        // Deleting a financial record is worth one confirmation step.
        var confirm = new ContentDialog
        {
            Title = "Delete expense",
            Content = $"Delete {row.ExpenseNumber} ({row.AmountText})? This cannot be undone.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close,
        }.Themed(XamlRoot);

        if (await confirm.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        try
        {
            await ViewModel.DeleteAsync(row.Id);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }

        await ViewModel.SearchAsync();
    }
}
