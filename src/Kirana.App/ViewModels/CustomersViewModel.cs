using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Customers;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Management-facing customer and Udhaar overview. The list is a read-only projection;
/// all payment, audit, and balance rules remain in <see cref="ICustomerCreditService"/>.</summary>
public sealed partial class CustomersViewModel(
    ICustomerService customerService,
    ICustomerCreditService creditService,
    ManagementSession session) : ObservableObject
{
    private readonly List<CustomerRowViewModel> _allCustomers = [];

    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private bool _outstandingOnly;
    [ObservableProperty] private string _selectedStatusFilter = "All customers";
    [ObservableProperty] private string _selectedSortOption = "Name";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _totalCustomersText = "0";
    [ObservableProperty] private decimal _totalOutstanding;
    [ObservableProperty] private string _overdueCustomersText = "0";
    [ObservableProperty] private string _newCustomersThisMonthText = "0";

    public bool CanManageCustomers => session.HasPermission(PermissionKeys.CustomersManage);
    public int? CurrentUserId => session.CurrentUser?.Id;
    public ObservableCollection<CustomerRowViewModel> Customers { get; } = [];
    public IReadOnlyList<string> StatusFilterOptions { get; } = ["All customers", "Active", "Inactive", "Overdue"];
    public IReadOnlyList<string> SortOptions { get; } = ["Name", "Outstanding", "Last purchase"];
    public bool HasResults => Customers.Count > 0;

    public async Task InitializeAsync() => await SearchAsync();

    [RelayCommand]
    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await creditService.SearchOverviewAsync(new CustomerSearchQuery
            {
                SearchText = SearchText,
                IncludeInactive = true,
                MaxResults = 1000,
            }, CurrentUserId);

            _allCustomers.Clear();
            _allCustomers.AddRange(results.Select(ToRow));
            UpdateSummary();

            IEnumerable<CustomerRowViewModel> filtered = _allCustomers;
            filtered = SelectedStatusFilter switch
            {
                "Active" => filtered.Where(x => x.IsActive),
                "Inactive" => filtered.Where(x => !x.IsActive),
                "Overdue" => filtered.Where(x => x.IsOverdue),
                _ => filtered,
            };
            if (OutstandingOnly)
            {
                filtered = filtered.Where(x => x.HasOutstanding);
            }

            filtered = SelectedSortOption switch
            {
                "Outstanding" => filtered.OrderByDescending(x => x.OutstandingBalance).ThenBy(x => x.Name),
                "Last purchase" => filtered.OrderByDescending(x => x.LastPurchaseDateUtc).ThenBy(x => x.Name),
                _ => filtered.OrderBy(x => x.Name),
            };

            Customers.Clear();
            foreach (var customer in filtered)
            {
                Customers.Add(customer);
            }
            OnPropertyChanged(nameof(HasResults));
        }
        catch (Exception ex)
        {
            Customers.Clear();
            OnPropertyChanged(nameof(HasResults));
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public Task<Customer> CreateCustomerAsync(CreateCustomerRequest request) => customerService.CreateAsync(request);
    public Task<Customer> UpdateCustomerAsync(int customerId, UpdateCustomerRequest request) => customerService.UpdateAsync(customerId, request);
    public Task<Customer> SetActiveAsync(int customerId, bool isActive) => customerService.SetActiveAsync(customerId, isActive, CurrentUserId);
    public Task<Customer?> GetCustomerAsync(int customerId) => customerService.GetByIdAsync(customerId);

    public async Task ShowAllAsync()
    {
        OutstandingOnly = false;
        SelectedStatusFilter = "All customers";
        await SearchAsync();
    }

    public async Task ShowOutstandingAsync()
    {
        OutstandingOnly = true;
        SelectedStatusFilter = "All customers";
        await SearchAsync();
    }

    public async Task ShowOverdueAsync()
    {
        OutstandingOnly = false;
        SelectedStatusFilter = "Overdue";
        await SearchAsync();
    }

    private void UpdateSummary()
    {
        TotalCustomersText = _allCustomers.Count.ToString("N0");
        TotalOutstanding = _allCustomers.Where(x => x.IsActive).Sum(x => x.OutstandingBalance);
        OverdueCustomersText = _allCustomers.Count(x => x.IsActive && x.IsOverdue).ToString("N0");
        var monthStart = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);
        NewCustomersThisMonthText = _allCustomers.Count(x => x.CreatedAtUtc >= monthStart).ToString("N0");
    }

    private CustomerRowViewModel ToRow(CustomerOverview customer) => new()
    {
        Id = customer.Id,
        CustomerCode = customer.CustomerCode,
        Name = customer.Name,
        Phone = customer.Phone,
        Address = customer.Address,
        Gstin = customer.Gstin,
        Notes = customer.Notes,
        OutstandingBalance = customer.OutstandingBalance,
        IsActive = customer.IsActive,
        CanManageCustomers = CanManageCustomers,
        CreatedAtUtc = customer.CreatedAtUtc,
        OldestOpenCreditDateUtc = customer.OldestOpenCreditDateUtc,
        LastPurchaseDateUtc = customer.LastPurchaseDateUtc,
        LastPaymentDateUtc = customer.LastPaymentDateUtc,
        LifetimePurchaseValue = customer.LifetimePurchaseValue,
    };
}
