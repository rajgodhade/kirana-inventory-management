using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Application.Inventories;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs the management Home dashboard. Every figure is permission-scoped: a user who cannot see
/// purchase or customer financials simply does not get those cards, matching what the nav pane
/// offers them. Read-only aggregates — this screen never mutates anything.
/// </summary>
public sealed partial class ManagementHomeViewModel(
    IKiranaDbContext db, IInventoryService inventoryService, ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _greeting = "Welcome";

    [ObservableProperty]
    private string _todaySalesText = "₹0.00";

    [ObservableProperty]
    private string _todaySalesCountText = "No sales yet today";

    [ObservableProperty]
    private string _lowStockCountText = "0";

    [ObservableProperty]
    private string _outstandingUdhaarText = "₹0.00";

    [ObservableProperty]
    private string _supplierDueText = "₹0.00";

    [ObservableProperty]
    private bool _isBusy;

    public bool CanSeeCustomerFinancials => session.HasPermission(PermissionKeys.CustomersManage);

    public bool CanSeePurchaseFinancials => session.HasPermission(PermissionKeys.PurchasesManage);

    public bool CanSeeInventory => session.HasPermission(PermissionKeys.InventoryManage);

    public ObservableCollection<RecentActivityRowViewModel> RecentActivity { get; } = [];

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            Greeting = BuildGreeting(session.CurrentUser?.FullName);

            var todayStartUtc = DateTime.UtcNow.Date;
            var todaySales = await db.Sales.AsNoTracking()
                .Where(s => s.SaleDateUtc >= todayStartUtc)
                .Select(s => s.GrandTotal)
                .ToListAsync();

            TodaySalesText = FormatCurrency(todaySales.Sum());
            TodaySalesCountText = todaySales.Count switch
            {
                0 => "No sales yet today",
                1 => "1 sale today",
                _ => $"{todaySales.Count} sales today",
            };

            if (CanSeeInventory)
            {
                // Reuses the Phase 2 low-stock rule rather than restating the threshold logic here.
                LowStockCountText = (await inventoryService.GetLowStockProductsAsync()).Count.ToString();
            }

            if (CanSeeCustomerFinancials)
            {
                OutstandingUdhaarText = FormatCurrency(await db.CustomerCredits.AsNoTracking()
                    .Where(c => c.RemainingAmount > 0)
                    .SumAsync(c => (decimal?)c.RemainingAmount) ?? 0m);
            }

            if (CanSeePurchaseFinancials)
            {
                SupplierDueText = FormatCurrency(await db.Suppliers.AsNoTracking()
                    .Where(s => s.OutstandingBalance > 0)
                    .SumAsync(s => (decimal?)s.OutstandingBalance) ?? 0m);
            }

            await LoadRecentActivityAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadRecentActivityAsync()
    {
        RecentActivity.Clear();

        var sales = await db.Sales.AsNoTracking()
            .OrderByDescending(s => s.SaleDateUtc)
            .Take(6)
            .Select(s => new { s.InvoiceNumber, s.GrandTotal, s.SaleDateUtc })
            .ToListAsync();

        foreach (var sale in sales)
        {
            RecentActivity.Add(new RecentActivityRowViewModel
            {
                Title = sale.InvoiceNumber,
                Subtitle = sale.SaleDateUtc.ToLocalTime().ToString("dd-MMM hh:mm tt"),
                Amount = FormatCurrency(sale.GrandTotal),
            });
        }
    }

    private static string FormatCurrency(decimal amount) =>
        "₹" + amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));

    private static string BuildGreeting(string? name)
    {
        var hour = DateTime.Now.Hour;
        var partOfDay = hour < 12 ? "Good morning" : hour < 17 ? "Good afternoon" : "Good evening";
        return string.IsNullOrWhiteSpace(name) ? partOfDay : $"{partOfDay}, {name.Split(' ')[0]}";
    }
}

/// <summary>One row in the Home screen's recent-activity list.</summary>
public sealed class RecentActivityRowViewModel
{
    public string Title { get; set; } = string.Empty;

    public string Subtitle { get; set; } = string.Empty;

    public string Amount { get; set; } = string.Empty;
}
