using Kirana.App.Theming;
using Kirana.App.ViewModels;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Application.Printing;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

public sealed partial class ManagementHomePage : Page
{
    public ManagementHomeViewModel ViewModel { get; }

    public ManagementHomePage()
    {
        var services = App.Services;
        ViewModel = new ManagementHomeViewModel(
            services.GetRequiredService<IKiranaDbContext>(),
            services.GetRequiredService<IInventoryService>(),
            services.GetRequiredService<ManagementSession>());

        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.InitializeAsync();
    }

    private void OnBackToBillingClick(object sender, RoutedEventArgs e)
    {
        // Returns to billing without locking — the session stays open so the user can come back.
        App.RootFrame?.Navigate(typeof(PosShellPage));
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
