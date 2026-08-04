using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Authentication;
using Kirana.Application.Customers;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs the Customers management page (PRD §30-31). Master data comes from
/// <see cref="ICustomerService"/>; the outstanding figures shown on this screen come from
/// <see cref="ICustomerCreditService"/>, which enforces <see cref="PermissionKeys.CustomersManage"/>
/// server-side — <see cref="CanManageCustomers"/> only drives what the UI offers.
/// </summary>
public sealed partial class CustomersViewModel(
    ICustomerService customerService,
    ICustomerCreditService creditService,
    ManagementSession session) : ObservableObject
{
    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _showInactive;

    [ObservableProperty]
    private bool _showOnlyOutstanding;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TotalOutstandingDisplay))]
    private decimal _totalOutstanding;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingCustomerCountDisplay))]
    private int _outstandingCustomerCount;

    public bool CanManageCustomers => session.HasPermission(PermissionKeys.CustomersManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public ObservableCollection<CustomerRowViewModel> Customers { get; } = [];

    public string TotalOutstandingDisplay => TotalOutstanding.ToString("0.00");

    public string OutstandingCustomerCountDisplay => OutstandingCustomerCount == 1
        ? "1 customer owes"
        : $"{OutstandingCustomerCount} customers owe";

    public async Task InitializeAsync()
    {
        await SearchAsync();
        await RefreshOutstandingSummaryAsync();
    }

    [RelayCommand]
    public async Task SearchAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var results = await customerService.SearchAsync(new CustomerSearchQuery
            {
                SearchText = SearchText,
                IncludeInactive = ShowInactive,
                MaxResults = 200,
            });

            Customers.Clear();
            foreach (var customer in results)
            {
                if (ShowOnlyOutstanding && customer.CreditBalance <= 0)
                {
                    continue;
                }

                Customers.Add(ToRow(customer));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Totals for the Outstanding Summary strip. Derived from the credit ledger rather than
    /// the denormalized balances, so it stays correct even if a balance were ever to drift.</summary>
    public async Task RefreshOutstandingSummaryAsync()
    {
        if (!CanManageCustomers)
        {
            TotalOutstanding = 0;
            OutstandingCustomerCount = 0;
            return;
        }

        try
        {
            var summary = await creditService.GetOutstandingSummaryAsync(CurrentUserId);
            TotalOutstanding = summary.Sum(s => s.OutstandingAmount);
            OutstandingCustomerCount = summary.Count;
        }
        catch (UnauthorizedAccessException)
        {
            TotalOutstanding = 0;
            OutstandingCustomerCount = 0;
        }
    }

    public Task<Customer> CreateCustomerAsync(CreateCustomerRequest request) => customerService.CreateAsync(request);

    public Task<Customer> UpdateCustomerAsync(int customerId, UpdateCustomerRequest request) =>
        customerService.UpdateAsync(customerId, request);

    public Task<Customer> SetActiveAsync(int customerId, bool isActive) =>
        customerService.SetActiveAsync(customerId, isActive, CurrentUserId);

    public Task<Customer?> GetCustomerAsync(int customerId) => customerService.GetByIdAsync(customerId);

    private static CustomerRowViewModel ToRow(Customer customer) => new()
    {
        Id = customer.Id,
        CustomerCode = customer.CustomerCode,
        Name = customer.Name,
        Phone = customer.Phone,
        Address = customer.Address,
        CreditBalance = customer.CreditBalance,
        IsActive = customer.IsActive,
    };
}
