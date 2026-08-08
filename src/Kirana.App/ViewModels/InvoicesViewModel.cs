using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Billing;
using Kirana.Application.Customers;
using Kirana.Application.Reports;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Read-only completed-invoice workspace. Billing remains responsible for creating sales.</summary>
public sealed partial class InvoicesViewModel(
    IInvoiceService invoiceService,
    ICustomerService customerService,
    ManagementSession session) : ObservableObject
{
    private static readonly Customer AllCustomersOption = new() { Id = 0, Name = "All customers" };
    private static readonly InvoicePartyFilterOption AllCashiersOption = new(0, "All cashiers");

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private DateTimeOffset? _fromDateFilter;
    [ObservableProperty] private DateTimeOffset? _toDateFilter;
    [ObservableProperty] private string _selectedPaymentMethod = "All payment methods";
    [ObservableProperty] private string _selectedPromotionFilter = "All promotions";
    [ObservableProperty] private string _selectedStatusFilter = "All statuses";
    [ObservableProperty] private string _selectedSortOption = "Newest";
    [ObservableProperty] private Customer? _selectedCustomerFilter;
    [ObservableProperty] private InvoicePartyFilterOption? _selectedCashierFilter;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private decimal _todaySales;
    [ObservableProperty] private string _todayBillsText = "0";
    [ObservableProperty] private decimal _averageBillValue;
    [ObservableProperty] private decimal _selectedPeriodSales;

    public int? CurrentUserId => session.CurrentUser?.Id;
    public bool CanRefund => session.HasPermission(PermissionKeys.SalesProcessRefund);
    public bool CanReprint => session.HasPermission(PermissionKeys.SalesReprintInvoice);
    public bool CanExport => session.HasPermission(PermissionKeys.ReportsView);
    public bool HasResults => Invoices.Count > 0;
    public ObservableCollection<InvoiceRowViewModel> Invoices { get; } = [];
    public ObservableCollection<Customer> CustomerFilterOptions { get; } = [];
    public ObservableCollection<InvoicePartyFilterOption> CashierFilterOptions { get; } = [];
    public IReadOnlyList<string> PaymentMethodOptions { get; } = ["All payment methods", "Cash", "Upi", "Card", "CustomerCredit"];
    public IReadOnlyList<string> PromotionFilterOptions { get; } = ["All promotions", "With promotion", "Without promotion"];
    public IReadOnlyList<string> StatusFilterOptions { get; } = ["All statuses", "Completed"];
    public IReadOnlyList<string> SortOptions { get; } = ["Newest", "Oldest", "Amount: high to low", "Amount: low to high"];

    public async Task InitializeAsync()
    {
        if (CashierFilterOptions.Count == 0)
        {
            CashierFilterOptions.Add(AllCashiersOption);
            SelectedCashierFilter = AllCashiersOption;
        }

        await LoadCustomerFiltersAsync();
        await SearchAsync();
    }

    private async Task LoadCustomerFiltersAsync()
    {
        CustomerFilterOptions.Clear();
        CustomerFilterOptions.Add(AllCustomersOption);
        SelectedCustomerFilter = AllCustomersOption;
        foreach (var customer in await customerService.SearchAsync(new CustomerSearchQuery { IncludeInactive = true, MaxResults = 1000 }))
        {
            CustomerFilterOptions.Add(customer);
        }
    }

    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await invoiceService.SearchAsync(new InvoiceSearchQuery
            {
                SearchText = SearchText,
                FromUtc = ToLocalDayStartUtc(FromDateFilter),
                ToUtc = ToLocalDayEndUtc(ToDateFilter),
                PaymentMethod = Enum.TryParse<PaymentMethod>(SelectedPaymentMethod, out var method) ? method : null,
                CustomerId = SelectedCustomerFilter is { Id: > 0 } customer ? customer.Id : null,
                CashierId = SelectedCashierFilter is { Id: > 0 } cashier ? cashier.Id : null,
                HasPromotion = SelectedPromotionFilter switch { "With promotion" => true, "Without promotion" => false, _ => null },
                Status = SelectedStatusFilter == "Completed" ? SaleStatus.Completed : null,
                SortBy = SelectedSortOption switch
                {
                    "Oldest" => InvoiceSortBy.Oldest,
                    "Amount: high to low" => InvoiceSortBy.AmountHighToLow,
                    "Amount: low to high" => InvoiceSortBy.AmountLowToHigh,
                    _ => InvoiceSortBy.Newest,
                },
            }, CurrentUserId);

            Invoices.Clear();
            foreach (var invoice in results)
            {
                Invoices.Add(new InvoiceRowViewModel
                {
                    SaleId = invoice.SaleId, InvoiceNumber = invoice.InvoiceNumber, CustomerId = invoice.CustomerId,
                    CustomerName = invoice.CustomerName, CustomerPhoneText = invoice.CustomerPhone ?? "Walk-in sale",
                    CashierUserId = invoice.CashierUserId, CashierName = invoice.CashierName, SaleDateUtc = invoice.SaleDateUtc,
                    TotalItems = invoice.TotalItems, PaymentMethodText = invoice.PaymentMethodText,
                    PromotionText = invoice.PromotionText, GrandTotal = invoice.GrandTotal, StatusText = invoice.Status.ToString(),
                });
            }
            RefreshCashierOptions();
            RecalculateKpis();
            OnPropertyChanged(nameof(HasResults));
        }
        catch (Exception ex)
        {
            Invoices.Clear();
            OnPropertyChanged(nameof(HasResults));
            ErrorMessage = ex.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task ShowTodayAsync()
    {
        var today = DateTimeOffset.Now.Date;
        FromDateFilter = today;
        ToDateFilter = today;
        await SearchAsync();
    }

    public async Task ClearDateFiltersAsync()
    {
        FromDateFilter = null;
        ToDateFilter = null;
        await SearchAsync();
    }

    public async Task ClearFiltersAsync()
    {
        SearchText = string.Empty;
        FromDateFilter = null;
        ToDateFilter = null;
        SelectedPaymentMethod = "All payment methods";
        SelectedPromotionFilter = "All promotions";
        SelectedStatusFilter = "All statuses";
        SelectedSortOption = "Newest";
        SelectedCustomerFilter = AllCustomersOption;
        SelectedCashierFilter = AllCashiersOption;
        await SearchAsync();
    }

    private void RefreshCashierOptions()
    {
        var previousId = SelectedCashierFilter?.Id ?? 0;
        CashierFilterOptions.Clear();
        CashierFilterOptions.Add(AllCashiersOption);
        foreach (var cashier in Invoices.Where(i => i.CashierUserId is not null).GroupBy(i => i.CashierUserId!.Value)
                     .Select(group => new InvoicePartyFilterOption(group.Key, group.First().CashierName)).OrderBy(x => x.Name))
        {
            CashierFilterOptions.Add(cashier);
        }
        SelectedCashierFilter = CashierFilterOptions.FirstOrDefault(x => x.Id == previousId) ?? AllCashiersOption;
    }

    private void RecalculateKpis()
    {
        var today = DateTime.Today;
        var todayInvoices = Invoices.Where(i => i.SaleDateUtc.ToLocalTime().Date == today).ToList();
        TodaySales = todayInvoices.Sum(i => i.GrandTotal);
        TodayBillsText = todayInvoices.Count.ToString("N0");
        AverageBillValue = todayInvoices.Count == 0 ? 0 : todayInvoices.Average(i => i.GrandTotal);
        SelectedPeriodSales = Invoices.Sum(i => i.GrandTotal);
    }

    private static DateTime? ToLocalDayStartUtc(DateTimeOffset? selectedDate)
    {
        if (selectedDate is null) return null;

        var localDate = selectedDate.Value.Date;
        return TimeZoneInfo.ConvertTimeToUtc(localDate, TimeZoneInfo.Local);
    }

    private static DateTime? ToLocalDayEndUtc(DateTimeOffset? selectedDate)
    {
        if (selectedDate is null) return null;

        var localDateExclusiveEnd = selectedDate.Value.Date.AddDays(1);
        return TimeZoneInfo.ConvertTimeToUtc(localDateExclusiveEnd, TimeZoneInfo.Local).AddTicks(-1);
    }

    public ReportExportData BuildExportData() => new()
    {
        Title = "Invoices",
        Subtitle = "Completed sales in the selected view",
        Columns = ["Invoice", "Customer", "Mobile", "Cashier", "Date", "Items", "Payment", "Promotion", "Total", "Status"],
        Rows = Invoices.Select(invoice => (IReadOnlyList<string>)
        [invoice.InvoiceNumber, invoice.CustomerName, invoice.CustomerPhoneText, invoice.CashierName,
         invoice.DateTimeText, invoice.TotalItemsText, invoice.PaymentMethodText, invoice.PromotionText ?? "",
         $"₹{invoice.GrandTotal:0.00}", invoice.StatusText]).ToList(),
    };
}

public sealed record InvoicePartyFilterOption(int Id, string Name);
