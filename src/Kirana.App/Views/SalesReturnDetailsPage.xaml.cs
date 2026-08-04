using Kirana.App.Printing;
using Kirana.Application.Authentication;
using Kirana.Application.Printing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

/// <summary>
/// Read-only view of one sales return. Reuses <see cref="ReturnReceiptDocument"/> as its view model
/// rather than introducing a parallel shape — the screen and the printed slip should never be able
/// to disagree about what was returned.
/// </summary>
public sealed partial class SalesReturnDetailsPage : Page
{
    private int _salesReturnId;

    public SalesReturnDetailsPage() => InitializeComponent();

    private int? CurrentUserId => App.Services.GetRequiredService<ManagementSession>().CurrentUser?.Id;

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _salesReturnId = (int)e.Parameter;
        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var document = await App.Services.GetRequiredService<IReturnReceiptService>()
                .GetReturnReceiptAsync(_salesReturnId, CurrentUserId);

            TitleText.Text = document.ReturnNumber;
            SubtitleText.Text = $"Against invoice {document.InvoiceNumber} · {document.ReturnDateUtc.ToLocalTime():dd-MMM-yyyy hh:mm tt}";
            TotalText.Text = Format(document.TotalReturnAmount);
            RefundText.Text = Format(document.RefundAmount);
            MethodText.Text = document.IsRefund ? document.RefundMethod : "No refund";
            CustomerText.Text = string.IsNullOrWhiteSpace(document.CustomerName)
                ? "Walk-in customer"
                : $"{document.CustomerName} · {document.CustomerCode}";
            ReasonText.Text = string.IsNullOrWhiteSpace(document.Reason) ? string.Empty : $"Reason: {document.Reason}";

            LinesList.ItemsSource = document.Lines;
        }
        catch (Exception ex)
        {
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
    }

    private async void OnPrintClick(object sender, RoutedEventArgs e)
    {
        var receiptService = App.Services.GetRequiredService<IReturnReceiptService>();
        try
        {
            var document = await receiptService.GetReturnReceiptAsync(_salesReturnId, CurrentUserId);

            using var helper = new ReturnReceiptPrintHelper(App.MainWindow, document, InvoiceFormat.Thermal80mm);
            await helper.ShowPrintUIAsync();

            await receiptService.LogReturnPrintAsync(_salesReturnId, CurrentUserId);
        }
        catch (Exception ex)
        {
            ErrorBar.Message = $"Could not print the receipt: {ex.Message}. The return is unaffected.";
            ErrorBar.IsOpen = true;
        }
    }

    private static string Format(decimal amount) =>
        "₹" + amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
}
