using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Expenses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class ExpenseCategoriesPage : Page
{
    public ExpenseCategoriesViewModel ViewModel { get; }

    public ExpenseCategoriesPage()
    {
        var services = App.Services;
        ViewModel = new ExpenseCategoriesViewModel(
            services.GetRequiredService<IExpenseCategoryService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private async void OnFilterChanged(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();

    private void OnBackToExpensesClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ExpensesPage));

    private async void OnAddClick(object sender, RoutedEventArgs e)
    {
        var (name, description, confirmed) = await PromptAsync("Add Category", string.Empty, string.Empty);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await ViewModel.CreateAsync(name, description);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }

        await ViewModel.LoadAsync();
    }

    private async void OnEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExpenseCategoryRowViewModel row)
        {
            return;
        }

        var (name, description, confirmed) = await PromptAsync($"Rename {row.Name}", row.Name, row.Description);
        if (!confirmed)
        {
            return;
        }

        try
        {
            await ViewModel.UpdateAsync(row.Id, name, description);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }

        await ViewModel.LoadAsync();
    }

    private async void OnToggleActiveClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExpenseCategoryRowViewModel row)
        {
            return;
        }

        try
        {
            await ViewModel.SetActiveAsync(row.Id, !row.IsActive);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = ex.Message;
        }

        await ViewModel.LoadAsync();
    }

    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ExpenseCategoryRowViewModel row)
        {
            return;
        }

        var confirm = new ContentDialog
        {
            Title = "Delete category",
            Content = $"Delete '{row.Name}'? Categories with expenses recorded against them cannot be deleted.",
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

        await ViewModel.LoadAsync();
    }

    /// <summary>
    /// A category is just a name plus a description, so it gets a lightweight inline dialog rather
    /// than its own dialog class. Built here rather than nested inside another dialog's handler —
    /// nested ContentDialogs terminate the app (learned in Phase 5).
    /// </summary>
    private async Task<(string Name, string? Description, bool Confirmed)> PromptAsync(
        string title, string name, string description)
    {
        var nameBox = new TextBox { Header = "Name *", Text = name };
        var descriptionBox = new TextBox { Header = "Description", Text = description };

        var panel = new StackPanel { Spacing = 10, MinWidth = 340 };
        panel.Children.Add(nameBox);
        panel.Children.Add(descriptionBox);

        var dialog = new ContentDialog
        {
            Title = title,
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
        }.Themed(XamlRoot);

        var result = await dialog.ShowAsync();
        return (nameBox.Text, descriptionBox.Text, result == ContentDialogResult.Primary);
    }
}
