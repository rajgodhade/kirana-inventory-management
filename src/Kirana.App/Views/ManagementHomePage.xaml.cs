using Kirana.App.Theming;
using Kirana.App.Services;
using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Backup;
using Kirana.Application.Inventories;
using Kirana.Application.Hardware;
using Kirana.Application.Printing;
using Kirana.Application.Reports;
using Kirana.Application.Promotions;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class ManagementHomePage : Page
{
    private readonly InvoiceRefreshNotifier _refreshNotifier;
    public ManagementHomeViewModel ViewModel { get; }

    public ManagementHomePage()
    {
        var services = App.Services;
        _refreshNotifier = services.GetRequiredService<InvoiceRefreshNotifier>();
        ViewModel = new ManagementHomeViewModel(
            services.GetRequiredService<IKiranaDbContext>(),
            services.GetRequiredService<IInventoryService>(),
            services.GetRequiredService<IDashboardService>(),
            services.GetRequiredService<ISalesReportService>(),
            services.GetRequiredService<IProductReportService>(),
            services.GetRequiredService<IBackupService>(),
            services.GetRequiredService<IHardwareMonitor>(),
            services.GetRequiredService<IHardwareSettingsService>(),
            services.GetRequiredService<IPromotionService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _refreshNotifier.InvoicesChanged += OnInvoicesChanged;
        await ViewModel.InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) => _refreshNotifier.InvoicesChanged -= OnInvoicesChanged;

    private void OnInvoicesChanged(object? sender, EventArgs e) =>
        _ = DispatcherQueue.TryEnqueue(async () => await ViewModel.RefreshRecentInvoicesAsync());

    private void OnBackToBillingClick(object sender, RoutedEventArgs e)
    {
        // Returns to billing without locking — the session stays open so the user can come back.
        App.RootFrame?.Navigate(typeof(PosShellPage));
    }

    private void OnDashboardDestinationClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not string route)
        {
            return;
        }

        var parts = route.Split(':', 2);
        var destination = parts[0];
        object? parameter = destination == "Products" && parts.Length == 2
            ? parts[1] switch
            {
                "LowStock" => ProductListNavigationFilter.LowStock,
                "OutOfStock" => ProductListNavigationFilter.OutOfStock,
                _ => ProductListNavigationFilter.All,
            }
            : null;

        var pageType = destination switch
        {
            "Products" => typeof(ProductsPage),
            "Promotions" => typeof(PromotionsPage),
            "Customers" => typeof(CustomersPage),
            "Invoices" => typeof(InvoicesPage),
            "Purchases" => typeof(PurchasesPage),
            "PurchaseOrders" => typeof(PurchaseOrdersPage),
            "PurchaseEntry" => typeof(PurchaseEntryPage),
            "Expenses" => typeof(ExpensesPage),
            "Reports" => typeof(ReportsHubPage),
            "Backup" => typeof(BackupManagerPage),
            _ => null,
        };

        if (pageType is not null && Frame?.CurrentSourcePageType != pageType)
        {
            Frame?.Navigate(pageType, parameter);
        }
    }

    /// <summary>
    /// Reprint is a sequence of dialogs rather than a destination, so it lives here as a quick
    /// action. The dialogs are shown one after another, never nested — a ContentDialog opened from
    /// inside another one's button handler kills the app (learned in Phase 5).
    /// </summary>
    private async void OnReprintInvoiceClick(object sender, RoutedEventArgs e)
    {
        var invoicePrintService = App.Services.GetRequiredService<IInvoicePrintService>();

        var searchDialog = new ReprintInvoiceDialog(invoicePrintService).Themed(XamlRoot);
        var searchResult = await searchDialog.ShowAsync();

        if (searchResult != ContentDialogResult.Primary || searchDialog.FoundDocument is not { } document)
        {
            return;
        }

        var session = App.Services.GetRequiredService<ManagementSession>();
        int userId;

        if (session.RequirePinForReprint)
        {
            var authService = App.Services.GetRequiredService<IAuthenticationService>();
            var authDialog = new ManagerAuthorizationDialog(authService, PermissionKeys.SalesReprintInvoice).Themed(XamlRoot);
            var authResult = await authDialog.ShowAsync();

            if (authResult != ContentDialogResult.Primary || authDialog.AuthorizedUserId is not { } authorizedUserId)
            {
                return;
            }

            userId = authorizedUserId;
        }
        else
        {
            // Reached this page at all only via an unlocked Dashboard session, so a current user
            // always exists here.
            userId = session.CurrentUser!.Id;
        }

        var db = App.Services.GetRequiredService<IKiranaDbContext>();
        var store = await db.Stores.FirstOrDefaultAsync();
        var defaultFormat = InvoiceLayoutCalculator.ParseFormat(store?.DefaultInvoiceFormat);

        var previewViewModel = new InvoicePreviewViewModel(document, defaultFormat, userId, isReprint: true, invoicePrintService);
        var previewDialog = new InvoicePreviewDialog(previewViewModel).Themed(XamlRoot);
        previewDialog.Title = $"Reprint — Invoice {document.InvoiceNumber}";
        await previewDialog.ShowAsync();
    }
}
