using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Printing;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class ManagementPlaceholderPage : Page
{
    private readonly ManagementSession _session;

    public bool CanManageUsers { get; }
    public bool CanViewAuditLog { get; }
    public bool CanChangeSettings { get; }
    public bool CanViewReports { get; }
    public bool CanManagePurchases { get; }

    public ManagementPlaceholderPage()
    {
        _session = App.Services.GetRequiredService<ManagementSession>();
        CanManageUsers = _session.HasPermission(PermissionKeys.UsersManage);
        CanViewAuditLog = _session.HasPermission(PermissionKeys.AuditLogView);
        CanChangeSettings = _session.HasPermission(PermissionKeys.SettingsChange);
        CanViewReports = _session.HasPermission(PermissionKeys.ReportsView);
        CanManagePurchases = _session.HasPermission(PermissionKeys.PurchasesManage);

        InitializeComponent();
    }

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        var authService = App.Services.GetRequiredService<IAuthenticationService>();
        authService.LockAndReturnToBilling();
        Frame.Navigate(typeof(PosShellPage));
    }

    private void OnProductsClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(ProductsPage));

    private void OnBarcodeScanClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(BarcodeScanTestPage));

    private void OnSuppliersClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SuppliersPage));

    private void OnPurchasesClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(PurchasesPage));

    private void OnUsersClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(UserManagementPage));

    private void OnAuditLogClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(AuditLogPage));

    private void OnSettingsClick(object sender, RoutedEventArgs e) => Frame.Navigate(typeof(SettingsPage));

    private async void OnReportsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Reports",
            Content = "The full reports/dashboard screen arrives in a later phase.",
            CloseButtonText = "OK",
        };
        await dialog.ShowAsync();
    }

    private async void OnReprintInvoiceClick(object sender, RoutedEventArgs e)
    {
        var invoicePrintService = App.Services.GetRequiredService<IInvoicePrintService>();

        var searchDialog = new ReprintInvoiceDialog(invoicePrintService) { XamlRoot = XamlRoot };
        var searchResult = await searchDialog.ShowAsync();

        if (searchResult != ContentDialogResult.Primary || searchDialog.FoundDocument is not { } document)
        {
            return;
        }

        var authService = App.Services.GetRequiredService<IAuthenticationService>();
        var authDialog = new ManagerAuthorizationDialog(authService, PermissionKeys.SalesReprintInvoice) { XamlRoot = XamlRoot };
        var authResult = await authDialog.ShowAsync();

        if (authResult != ContentDialogResult.Primary || authDialog.AuthorizedUserId is not { } userId)
        {
            return;
        }

        var db = App.Services.GetRequiredService<IKiranaDbContext>();
        var store = await db.Stores.FirstOrDefaultAsync();
        var defaultFormat = InvoiceLayoutCalculator.ParseFormat(store?.DefaultInvoiceFormat);

        var previewViewModel = new InvoicePreviewViewModel(document, defaultFormat, userId, isReprint: true, invoicePrintService);
        var previewDialog = new InvoicePreviewDialog(previewViewModel)
        {
            XamlRoot = XamlRoot,
            Title = $"Reprint — Invoice {document.InvoiceNumber}",
        };
        await previewDialog.ShowAsync();
    }
}
