using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the customer search / quick-add picker opened from the POS (PRD §17, F4).</summary>
public sealed partial class CustomerPickerViewModel : ObservableObject
{
    private readonly PosShellViewModel _owner;

    public ObservableCollection<Customer> Results { get; } = [];

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _newCustomerName = string.Empty;

    [ObservableProperty]
    private string _newCustomerPhone = string.Empty;

    public Customer? SelectedCustomer { get; private set; }

    public CustomerPickerViewModel(PosShellViewModel owner)
    {
        _owner = owner;
    }

    public async Task SearchAsync()
    {
        ErrorMessage = null;
        var results = await _owner.SearchCustomersAsync(SearchText);
        Results.Clear();
        foreach (var customer in results)
        {
            Results.Add(customer);
        }
    }

    public void Pick(Customer customer) => SelectedCustomer = customer;

    public void PickWalkIn() => SelectedCustomer = null;

    [RelayCommand]
    private async Task CreateCustomerAsync()
    {
        ErrorMessage = null;
        try
        {
            var customer = await _owner.CreateCustomerAsync(
                NewCustomerName,
                string.IsNullOrWhiteSpace(NewCustomerPhone) ? null : NewCustomerPhone,
                address: null);
            SelectedCustomer = customer;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
    }
}
