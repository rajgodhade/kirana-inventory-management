using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.Customers;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>
/// Backs the Customer Details screen (PRD §30-31): profile, outstanding balance, ledger, purchase
/// history and repayment history for one customer. Every financial list here comes from
/// <see cref="ICustomerCreditService"/>, so an unauthorized user gets an error from the service
/// rather than silently-empty lists.
/// </summary>
public sealed partial class CustomerLedgerViewModel(
    int customerId,
    ICustomerService customerService,
    ICustomerCreditService creditService,
    ManagementSession session) : ObservableObject
{
    public bool CanManageCustomers => session.HasPermission(PermissionKeys.CustomersManage);

    public int? CurrentUserId => session.CurrentUser?.Id;

    public int CustomerId => customerId;

    [ObservableProperty]
    private string _customerName = string.Empty;

    [ObservableProperty]
    private string _customerCode = string.Empty;

    [ObservableProperty]
    private string _phone = string.Empty;

    [ObservableProperty]
    private string _address = string.Empty;

    [ObservableProperty]
    private string _notes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingDisplay))]
    [NotifyPropertyChangedFor(nameof(HasOutstanding))]
    private decimal _outstandingBalance;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isBusy;

    public string OutstandingDisplay => $"₹{OutstandingBalance:0.00}";

    public bool HasOutstanding => OutstandingBalance > 0;

    public ObservableCollection<CustomerLedgerRowViewModel> Entries { get; } = [];

    public ObservableCollection<CustomerLedgerRowViewModel> Purchases { get; } = [];

    public ObservableCollection<CustomerLedgerRowViewModel> Repayments { get; } = [];

    public async Task InitializeAsync()
    {
        ErrorMessage = null;
        IsBusy = true;
        try
        {
            var customer = await customerService.GetByIdAsync(customerId)
                ?? throw new InvalidOperationException("Customer not found.");

            CustomerName = customer.Name;
            CustomerCode = customer.CustomerCode;
            Phone = customer.Phone ?? "—";
            Address = customer.Address ?? "—";
            Notes = customer.Notes ?? string.Empty;
            OutstandingBalance = customer.CreditBalance;

            await LoadLedgerAsync();
            await LoadPurchasesAsync();
            await LoadRepaymentsAsync();
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

    private async Task LoadLedgerAsync()
    {
        var ledger = await creditService.GetLedgerAsync(customerId, CurrentUserId);

        Entries.Clear();
        foreach (var entry in ledger.OrderByDescending(e => e.DateUtc))
        {
            Entries.Add(new CustomerLedgerRowViewModel
            {
                DateText = FormatDate(entry.DateUtc),
                EntryType = entry.EntryType,
                Reference = entry.Reference,
                DebitText = entry.DebitAmount > 0 ? $"₹{entry.DebitAmount:0.00}" : string.Empty,
                CreditText = entry.CreditAmount > 0 ? $"₹{entry.CreditAmount:0.00}" : string.Empty,
                RunningBalanceText = $"₹{entry.RunningBalance:0.00}",
                Notes = entry.Notes ?? string.Empty,
            });
        }
    }

    private async Task LoadPurchasesAsync()
    {
        var sales = await creditService.GetPurchaseHistoryAsync(customerId, CurrentUserId);

        Purchases.Clear();
        foreach (var sale in sales)
        {
            var creditPortion = sale.Payments
                .Where(p => p.Method == PaymentMethod.CustomerCredit)
                .Sum(p => p.Amount);

            Purchases.Add(new CustomerLedgerRowViewModel
            {
                Id = sale.Id,
                DateText = FormatDate(sale.SaleDateUtc),
                EntryType = creditPortion > 0 ? "Credit Sale" : "Paid Sale",
                Reference = sale.InvoiceNumber,
                DebitText = $"₹{sale.GrandTotal:0.00}",
                CreditText = creditPortion > 0 ? $"₹{creditPortion:0.00} on Udhaar" : "Fully paid",
            });
        }
    }

    private async Task LoadRepaymentsAsync()
    {
        var payments = await creditService.GetRepaymentHistoryAsync(customerId, CurrentUserId);

        Repayments.Clear();
        foreach (var payment in payments)
        {
            Repayments.Add(new CustomerLedgerRowViewModel
            {
                Id = payment.Id,
                DateText = FormatDate(payment.PaymentDateUtc),
                EntryType = payment.Method.ToString(),
                Reference = payment.ReceiptNumber,
                CreditText = $"₹{payment.Amount:0.00}",
                Notes = payment.Notes ?? string.Empty,
                CanPrintReceipt = true,
            });
        }
    }

    /// <summary>Records a repayment and reloads every list so the screen and the DB agree.
    /// Returns the new payment on success, or null with <see cref="ErrorMessage"/> set.</summary>
    public async Task<CreditPayment?> RecordRepaymentAsync(
        decimal amount, PaymentMethod method, string? referenceNumber, string? notes)
    {
        ErrorMessage = null;
        try
        {
            var payment = await creditService.RecordRepaymentAsync(new RecordCreditPaymentRequest
            {
                CustomerId = customerId,
                Amount = amount,
                Method = method,
                ReferenceNumber = referenceNumber,
                Notes = notes,
                RecordedByUserId = CurrentUserId,
            });

            await InitializeAsync();
            return payment;
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
            return null;
        }
    }

    private static string FormatDate(DateTime utc) => utc.ToLocalTime().ToString("dd-MMM-yyyy hh:mm tt");
}
