using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Promotions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace Kirana.App.Views;

public sealed partial class PromotionsPage : Page
{
    public PromotionsViewModel ViewModel { get; }
    public PromotionsPage()
    {
        ViewModel = new PromotionsViewModel(App.Services.GetRequiredService<IPromotionService>(), App.Services.GetRequiredService<ManagementSession>());
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.LoadAsync();
    }
    private async void OnCreateClick(object sender, RoutedEventArgs e) => await ShowEditorAsync(null);
    private async void OnEditClick(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.Tag is PromotionRowViewModel row) await ShowEditorAsync(row.Id); }
    private async void OnToggleClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PromotionRowViewModel row) return;
        try { await ViewModel.SetActiveAsync(row, !row.IsActive); await ViewModel.LoadAsync(); }
        catch (Exception ex) { var dialog = new ContentDialog { Title = "Promotion", Content = ex.Message, CloseButtonText = "Close" }.Themed(XamlRoot); await dialog.ShowAsync(); }
    }
    private async void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not PromotionRowViewModel row) return;
        var confirm = new ContentDialog { Title = "Delete promotion?", Content = $"Delete {row.Name}? Promotions already used on sales must be disabled instead.", PrimaryButtonText = "Delete", CloseButtonText = "Cancel", DefaultButton = ContentDialogButton.Close }.Themed(XamlRoot);
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try { await ViewModel.DeleteAsync(row); await ViewModel.LoadAsync(); }
        catch (Exception ex) { var dialog = new ContentDialog { Title = "Promotion", Content = ex.Message, CloseButtonText = "Close" }.Themed(XamlRoot); await dialog.ShowAsync(); }
    }
    private async Task ShowEditorAsync(int? id)
    {
        var dialog = new PromotionEditDialog(id).Themed(XamlRoot);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await ViewModel.LoadAsync();
    }
    private async void OnFilterClick(object sender, RoutedEventArgs e) => await ViewModel.LoadAsync();
    private async void OnSearchKeyDown(object sender, KeyRoutedEventArgs e) { if (e.Key == Windows.System.VirtualKey.Enter) await ViewModel.LoadAsync(); }
}
