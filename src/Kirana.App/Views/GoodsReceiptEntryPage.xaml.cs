using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Purchasing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

public sealed partial class GoodsReceiptEntryPage : Page
{
    public GoodsReceiptEntryViewModel ViewModel { get; }
    private int _purchaseOrderId;
    public GoodsReceiptEntryPage()
    {
        ViewModel = new(App.Services.GetRequiredService<IGoodsReceiptService>(), App.Services.GetRequiredService<ManagementSession>());
        InitializeComponent(); Loaded += async (_, _) =>
        {
            try { await ViewModel.InitializeAsync(_purchaseOrderId); }
            catch (Exception ex) { await new ContentDialog { Title = "Could not receive purchase order", Content = ex.Message, CloseButtonText = "Close" }.Themed(XamlRoot).ShowAsync(); Frame.GoBack(); }
        };
    }
    protected override void OnNavigatedTo(NavigationEventArgs e) { base.OnNavigatedTo(e); _purchaseOrderId = (int)e.Parameter; }
    private void OnQuantityChanged(object sender, TextChangedEventArgs e) => ViewModel.RefreshTotals();
    private void OnCancelClick(object sender, RoutedEventArgs e) { if (Frame.CanGoBack) Frame.GoBack(); else Frame.Navigate(typeof(PurchaseOrdersPage)); }
    private async void OnReviewClick(object sender, RoutedEventArgs e)
    {
        ViewModel.RefreshTotals(); var error = ViewModel.Validate();
        if (!string.IsNullOrEmpty(error)) { await new ContentDialog { Title = "Check received quantities", Content = error, CloseButtonText = "Close" }.Themed(XamlRoot).ShowAsync(); return; }
        var review = new StackPanel { Spacing = 8, MinWidth = 460 };
        review.Children.Add(new TextBlock { Text = $"PO: {ViewModel.PurchaseOrderNumber}\nSupplier: {ViewModel.SupplierName}", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        foreach (var line in ViewModel.Lines.Where(l => l.Received > 0)) review.Children.Add(new TextBlock { Text = $"{line.ProductName}: {line.Received:0.###} {line.Unit} (remaining {line.RemainingAfter:0.###})" });
        review.Children.Add(new TextBlock { Text = $"Total received: {ViewModel.TotalReceived:0.###}", FontWeight = Microsoft.UI.Text.FontWeights.Bold });
        var dialog = new ContentDialog { Title = "Review Goods Receipt", Content = review, PrimaryButtonText = "Complete GRN", CloseButtonText = "Back", DefaultButton = ContentDialogButton.Primary }.Themed(XamlRoot);
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var receipt = await ViewModel.CompleteAsync();
        if (receipt is null) { await new ContentDialog { Title = "Could not complete GRN", Content = ViewModel.ErrorMessage, CloseButtonText = "Close" }.Themed(XamlRoot).ShowAsync(); return; }
        var done = new ContentDialog { Title = $"{receipt.GoodsReceiptNumber} completed", Content = "Physical receipt is recorded. Inventory and supplier payable are unchanged until you finalize the Purchase.", PrimaryButtonText = "Create Purchase", CloseButtonText = "View Goods Receipts", DefaultButton = ContentDialogButton.Primary }.Themed(XamlRoot);
        if (await done.ShowAsync() == ContentDialogResult.Primary) Frame.Navigate(typeof(PurchaseEntryPage), receipt.Id); else Frame.Navigate(typeof(GoodsReceiptsPage));
    }
}
