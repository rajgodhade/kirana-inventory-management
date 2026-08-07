using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Promotions;
using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

public sealed partial class PromotionReportViewModel(IPromotionService promotionService, ManagementSession session) : ObservableObject
{
    public ObservableCollection<PromotionPerformanceRow> Rows { get; } = [];
    [ObservableProperty] private string _revenueText = "₹0.00";
    [ObservableProperty] private string _discountText = "₹0.00";
    [ObservableProperty] private string _salesText = "0";
    [ObservableProperty] private string _productsText = "0";
    [ObservableProperty] private string _mostUsedText = "—";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;

    public async Task LoadAsync()
    {
        IsBusy = true; ErrorMessage = null;
        try
        {
            var range = ReportDateRange.Resolve(ReportDatePreset.ThisMonth);
            var rows = await promotionService.GetPerformanceAsync(range.StartUtc, range.EndUtc, session.CurrentUser?.Id);
            Rows.Clear(); foreach (var row in rows) Rows.Add(row);
            RevenueText = Fmt(rows.Sum(x => x.Revenue)); DiscountText = Fmt(rows.Sum(x => x.DiscountGiven));
            SalesText = rows.Sum(x => x.SalesGenerated).ToString("N0"); ProductsText = rows.Sum(x => x.ProductsSold).ToString("N0");
            MostUsedText = rows.OrderByDescending(x => x.SalesGenerated).FirstOrDefault()?.PromotionName ?? "—";
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }
    private static string Fmt(decimal amount) => "₹" + amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
}
