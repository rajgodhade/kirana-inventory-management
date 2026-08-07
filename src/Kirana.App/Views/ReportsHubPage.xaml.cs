using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace Kirana.App.Views;

/// <summary>
/// The Dashboard &amp; Reports hub (PRD §51): one Pivot with the Dashboard as the default tab and
/// every report category alongside it, mirroring the tab layout <c>CustomerLedgerPage</c> already
/// established for Ledger/Purchase History/Payment History. Kept as a single hub rather than eight
/// separate nav entries so the management nav pane does not grow by eight items.
///
/// Every tab except Dashboard loads its data lazily, on first selection rather than when the page
/// opens — a Pivot builds every PivotItem's content immediately, so without this all eight tabs'
/// report queries would fire at once on every visit to Reports.
/// </summary>
public sealed partial class ReportsHubPage : Page
{
    private readonly HashSet<PivotItem> _loadedTabs = [];

    public ReportsHubPage()
    {
        InitializeComponent();

        var session = App.Services.GetRequiredService<ManagementSession>();
        ProfitPivotItem.Visibility = session.HasPermission(PermissionKeys.ReportsViewProfit)
            ? Microsoft.UI.Xaml.Visibility.Visible
            : Microsoft.UI.Xaml.Visibility.Collapsed;

        Loaded += (_, _) => _ = LoadSelectedTabAsync();
    }

    private async void OnTabSelected(object sender, SelectionChangedEventArgs e) => await LoadSelectedTabAsync();

    private async Task LoadSelectedTabAsync()
    {
        if (Tabs.SelectedItem is not PivotItem item || !_loadedTabs.Add(item))
        {
            return;
        }

        var task = item.Name switch
        {
            nameof(DashboardTab) => DashboardTab.EnsureLoadedAsync(),
            nameof(SalesTab) => SalesTab.EnsureLoadedAsync(),
            nameof(ProductsTab) => ProductsTab.EnsureLoadedAsync(),
            nameof(InventoryTab) => InventoryTab.EnsureLoadedAsync(),
            nameof(CustomersTab) => CustomersTab.EnsureLoadedAsync(),
            nameof(SuppliersTab) => SuppliersTab.EnsureLoadedAsync(),
            nameof(ExpensesTab) => ExpensesTab.EnsureLoadedAsync(),
            nameof(PromotionsTab) => PromotionsTab.EnsureLoadedAsync(),
            nameof(ProfitTab) => ProfitTab.EnsureLoadedAsync(),
            _ => Task.CompletedTask,
        };

        await task;
    }
}
