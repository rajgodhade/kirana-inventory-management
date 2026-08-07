using Kirana.App.Printing;
using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Authentication;
using Kirana.Application.Customers;
using Kirana.Application.Printing;
using Kirana.Application.Reports;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

public sealed partial class CustomerLedgerPage : Page
{
    private IReportExportService? _exportService;

    public CustomerLedgerViewModel ViewModel { get; private set; } = null!;

    public CustomerLedgerPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        var customerId = (int)e.Parameter;
        var services = App.Services;
        _exportService = services.GetRequiredService<IReportExportService>();
        ViewModel = new CustomerLedgerViewModel(
            customerId,
            services.GetRequiredService<ICustomerService>(),
            services.GetRequiredService<ICustomerCreditService>(),
            services.GetRequiredService<ManagementSession>());

        Bindings.Update();
        _ = ViewModel.InitializeAsync();
    }

    private async void OnRecordRepaymentClick(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanManageCustomers)
        {
            return;
        }

        var dialog = new CreditPaymentDialog(ViewModel).Themed(XamlRoot);
        await dialog.ShowAsync();

        // The repayment dialog must be fully closed before any print dialog opens — nested
        // ContentDialogs terminate the app silently (learned in Phase 5), so the print flow is
        // driven here by the caller rather than from inside the dialog.
        if (dialog.RecordedPayment is { } payment)
        {
            await PrintReceiptAsync(payment.Id);
        }
    }

    private async void OnPrintReceiptClick(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not CustomerLedgerRowViewModel row)
        {
            return;
        }

        await PrintReceiptAsync(row.Id);
    }

    /// <summary>Builds and prints the receipt. Printing is entirely separable from the repayment: a
    /// failure here is reported and never rolls back the recorded payment.</summary>
    private async Task PrintReceiptAsync(int creditPaymentId)
    {
        var services = App.Services;
        var receiptService = services.GetRequiredService<ICustomerReceiptService>();

        try
        {
            var document = await receiptService.GetReceiptAsync(creditPaymentId, ViewModel.CurrentUserId);

            using var helper = new CustomerReceiptPrintHelper(App.MainWindow, document, InvoiceFormat.Thermal80mm);
            await helper.ShowPrintUIAsync();

            await receiptService.LogPrintAsync(creditPaymentId, ViewModel.CurrentUserId);
        }
        catch (Exception ex)
        {
            ViewModel.ErrorMessage = $"Could not print the receipt: {ex.Message}. The repayment is saved and can be reprinted.";
        }
    }

    private async void OnPrintLedgerClick(object sender, RoutedEventArgs e) =>
        await ReportExportHelper.ExportToPdfAsync(App.MainWindow, ViewModel.BuildLedgerExportData(), _exportService!, ViewModel.CurrentUserId);

    private async void OnExportExcelClick(object sender, RoutedEventArgs e) =>
        await ReportExportHelper.ExportToFileAsync(App.MainWindow, ViewModel.BuildLedgerExportData(), ReportExportFormat.Excel, _exportService!, ViewModel.CurrentUserId);

    private async void OnExportPdfClick(object sender, RoutedEventArgs e) =>
        await ReportExportHelper.ExportToPdfAsync(App.MainWindow, ViewModel.BuildLedgerExportData(), _exportService!, ViewModel.CurrentUserId);
}
