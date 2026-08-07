using Kirana.App.Theming;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;

namespace Kirana.App.Views;

/// <summary>
/// The management area's persistent shell: a left <see cref="NavigationView"/> plus an app bar that
/// stay put while <see cref="ContentFrame"/> swaps screens. Replaces the old page-per-screen model
/// where every screen carried its own header and a "back to Management" button.
///
/// Nav items are removed — not merely disabled — when the signed-in user lacks the permission, so
/// the pane only advertises what that user can actually reach. This is presentation only; every
/// underlying service enforces the same permission independently.
/// </summary>
public sealed partial class ManagementShellPage : Page
{
    private readonly ManagementSession _session;
    private readonly ThemeService _themeService;

    public ManagementShellPage()
    {
        _session = App.Services.GetRequiredService<ManagementSession>();
        _themeService = App.Services.GetRequiredService<ThemeService>();

        InitializeComponent();

        ApplyPermissionsToPane();
        UpdateThemeIcon();

        // Keeping the back button in sync here means hosted pages never have to think about it.
        ContentFrame.Navigated += (_, _) =>
        {
            Nav.IsBackEnabled = ContentFrame.CanGoBack;
            SyncSelectedNavItem();
        };

        Loaded += OnLoaded;
    }

    /// <summary>Keeps the pane highlight on the section a drill-down screen belongs to, so going
    /// back from a customer ledger still shows "Customers" as selected.</summary>
    private void SyncSelectedNavItem()
    {
        var tag = ContentFrame.CurrentSourcePageType switch
        {
            var t when t == typeof(ManagementHomePage) => "Home",
            var t when t == typeof(ProductsPage) => "Products",
            var t when t == typeof(PromotionsPage) => "Promotions",
            var t when t == typeof(BarcodeScanTestPage) => "Barcodes",
            var t when t == typeof(CustomersPage) || t == typeof(CustomerLedgerPage) => "Customers",
            var t when t == typeof(SuppliersPage) || t == typeof(SupplierLedgerPage) => "Suppliers",
            var t when t == typeof(PurchasesPage) || t == typeof(PurchaseEntryPage) => "Purchases",
            var t when t == typeof(SalesReturnsPage) || t == typeof(NewSalesReturnPage)
                || t == typeof(SalesReturnDetailsPage) => "SalesReturns",
            var t when t == typeof(PurchaseReturnsPage) || t == typeof(NewPurchaseReturnPage)
                || t == typeof(PurchaseReturnDetailsPage) => "PurchaseReturns",
            var t when t == typeof(ExpensesPage) || t == typeof(ExpenseDetailsPage)
                || t == typeof(ExpenseCategoriesPage) => "Expenses",
            var t when t == typeof(UserManagementPage) => "Users",
            var t when t == typeof(AuditLogPage) => "Audit",
            var t when t == typeof(ReportsHubPage) => "Reports",
            var t when t == typeof(SettingsPage) => "Settings",
            var t when t == typeof(BackupManagerPage) => "BackupManager",
            var t when t == typeof(RestorePage) => "Restore",
            var t when t == typeof(ExportCenterPage) => "ExportCenter",
            var t when t == typeof(DatabaseMaintenancePage) => "DatabaseMaintenance",
            _ => null,
        };

        if (tag is null)
        {
            return;
        }

        var match = Nav.MenuItems.OfType<NavigationViewItem>().FirstOrDefault(i => (string?)i.Tag == tag);
        if (match is not null && !ReferenceEquals(Nav.SelectedItem, match))
        {
            Nav.SelectedItem = match;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        UserNameText.Text = _session.CurrentUser?.FullName ?? "Signed in";
        UserRoleText.Text = _session.CurrentUser?.Role?.Name ?? string.Empty;

        var db = App.Services.GetRequiredService<IKiranaDbContext>();
        var store = await db.Stores.AsNoTracking().FirstOrDefaultAsync();
        StoreNameText.Text = store?.Name ?? "Kirana";

        if (ContentFrame.Content is null)
        {
            Nav.SelectedItem = Nav.MenuItems.OfType<NavigationViewItem>().First();
            ContentFrame.Navigate(typeof(ManagementHomePage));
        }
    }

    /// <summary>Hides nav entries the current user has no permission for.</summary>
    private void ApplyPermissionsToPane()
    {
        SetVisible(NavCustomers, _session.HasPermission(PermissionKeys.CustomersManage));
        SetVisible(NavPromotions, _session.HasPermission(PermissionKeys.PromotionsView));
        SetVisible(NavSuppliers, _session.HasPermission(PermissionKeys.PurchasesManage));
        SetVisible(NavPurchases, _session.HasPermission(PermissionKeys.PurchasesManage));
        // Phase 9 adds no new permission keys: returns reuse the refund permission, purchase
        // returns reuse the purchasing permission, and expenses reuse the expenses permission.
        SetVisible(NavSalesReturns, _session.HasPermission(PermissionKeys.SalesProcessRefund));
        SetVisible(NavPurchaseReturns, _session.HasPermission(PermissionKeys.PurchasesManage));
        SetVisible(NavExpenses, _session.HasPermission(PermissionKeys.ExpensesManage));

        SetVisible(NavUsers, _session.HasPermission(PermissionKeys.UsersManage));
        SetVisible(NavAudit, _session.HasPermission(PermissionKeys.AuditLogView));
        SetVisible(NavReports, _session.HasPermission(PermissionKeys.ReportsView));
        SetVisible(NavSettings, _session.HasPermission(PermissionKeys.SettingsChange));

        // Backup, restore and database maintenance all share the one "dangerous data operation"
        // permission, which is Owner-only by default. The Export Center is deliberately looser: it
        // enforces a permission per data set, so a Manager who cannot restore can still export the
        // data they are already allowed to see.
        SetVisible(NavBackupManager, _session.HasPermission(PermissionKeys.BackupRestore));
        SetVisible(NavRestore, _session.HasPermission(PermissionKeys.BackupRestore));
        SetVisible(NavDatabaseMaintenance, _session.HasPermission(PermissionKeys.BackupRestore));
        SetVisible(NavExportCenter, CanExportAnything());

        // A group header with nothing under it reads as a broken pane, so drop it too.
        SetVisible(NavTradeHeader, NavCustomers.Visibility == Visibility.Visible
            || NavSuppliers.Visibility == Visibility.Visible
            || NavPurchases.Visibility == Visibility.Visible);

        SetVisible(NavReturnsHeader, NavSalesReturns.Visibility == Visibility.Visible
            || NavPurchaseReturns.Visibility == Visibility.Visible
            || NavExpenses.Visibility == Visibility.Visible);

        SetVisible(NavAdminHeader, NavUsers.Visibility == Visibility.Visible
            || NavAudit.Visibility == Visibility.Visible
            || NavReports.Visibility == Visibility.Visible
            || NavSettings.Visibility == Visibility.Visible);

        SetVisible(NavDataHeader, NavBackupManager.Visibility == Visibility.Visible
            || NavRestore.Visibility == Visibility.Visible
            || NavExportCenter.Visibility == Visibility.Visible
            || NavDatabaseMaintenance.Visibility == Visibility.Visible);
    }

    private bool CanExportAnything() =>
        _session.HasPermission(PermissionKeys.ProductsView)
        || _session.HasPermission(PermissionKeys.CustomersManage)
        || _session.HasPermission(PermissionKeys.PurchasesManage)
        || _session.HasPermission(PermissionKeys.InventoryManage)
        || _session.HasPermission(PermissionKeys.ReportsView)
        || _session.HasPermission(PermissionKeys.ExpensesManage);

    private static void SetVisible(NavigationViewItemBase item, bool visible) =>
        item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnNavSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItem is not NavigationViewItem { Tag: string tag })
        {
            return;
        }

        var target = tag switch
        {
            "Home" => typeof(ManagementHomePage),
            "Products" => typeof(ProductsPage),
            "Promotions" => typeof(PromotionsPage),
            "Barcodes" => typeof(BarcodeScanTestPage),
            "Customers" => typeof(CustomersPage),
            "Suppliers" => typeof(SuppliersPage),
            "Purchases" => typeof(PurchasesPage),
            "SalesReturns" => typeof(SalesReturnsPage),
            "PurchaseReturns" => typeof(PurchaseReturnsPage),
            "Expenses" => typeof(ExpensesPage),
            "Users" => typeof(UserManagementPage),
            "Audit" => typeof(AuditLogPage),
            "Reports" => typeof(ReportsHubPage),
            "Settings" => typeof(SettingsPage),
            "BackupManager" => typeof(BackupManagerPage),
            "Restore" => typeof(RestorePage),
            "ExportCenter" => typeof(ExportCenterPage),
            "DatabaseMaintenance" => typeof(DatabaseMaintenancePage),
            _ => null,
        };

        if (target is not null && ContentFrame.CurrentSourcePageType != target)
        {
            ContentFrame.Navigate(target, null, new EntranceNavigationTransitionInfo());
        }
    }

    /// <summary>
    /// Drill-down screens (a supplier ledger, a customer ledger, purchase entry) are pushed onto
    /// <see cref="ContentFrame"/> by their parent screen; the shell's back button pops them.
    /// </summary>
    private void OnNavBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private async void OnThemeToggleClick(object sender, RoutedEventArgs e)
    {
        await _themeService.ToggleAsync();
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon()
    {
        // Show the theme you'd switch *to*, which is the convention users expect from a toggle.
        //  = Brightness (sun),  = Quiet Hours (moon) in Segoe MDL2 Assets.
        ThemeIcon.Glyph = _themeService.IsEffectivelyDark ? "" : "";
        ToolTipService.SetToolTip(ThemeIcon, _themeService.IsEffectivelyDark ? "Switch to light" : "Switch to dark");
    }

    private void OnLockClick(object sender, RoutedEventArgs e)
    {
        App.Services.GetRequiredService<IAuthenticationService>().LockAndReturnToBilling();
        Frame.Navigate(typeof(PosShellPage));
    }
}
