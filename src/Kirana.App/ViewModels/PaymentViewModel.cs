using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Billing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Payment dialog (PRD §20) — Cash/UPI/Card/Customer Credit, with split
/// payment support. Completing here <em>is</em> completing the sale: once the payment lines
/// sum to the grand total, "Complete Sale" calls <see cref="ISaleService.CompleteSaleAsync"/>.</summary>
public sealed partial class PaymentViewModel : ObservableObject
{
    private readonly PosShellViewModel _owner;
    private readonly ISaleService _saleService;

    public decimal GrandTotal { get; }
    public IReadOnlyList<PaymentMethod> AvailableMethods { get; } = Enum.GetValues<PaymentMethod>();

    public ObservableCollection<PaymentLineViewModel> PaymentLines { get; } = [];

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private decimal _remainingAmount;

    public Sale? CompletedSale { get; private set; }

    public PaymentViewModel(PosShellViewModel owner, ISaleService saleService)
    {
        _owner = owner;
        _saleService = saleService;
        GrandTotal = owner.GrandTotal;

        PaymentLines.Add(new PaymentLineViewModel { AmountText = GrandTotal.ToString("0.00"), AmountTenderedText = GrandTotal.ToString("0.00") });
        RecalculateRemaining();
    }

    [RelayCommand]
    private void AddPaymentLine()
    {
        PaymentLines.Add(new PaymentLineViewModel
        {
            AmountText = RemainingAmount > 0 ? RemainingAmount.ToString("0.00") : "0",
        });
        RecalculateRemaining();
    }

    public void RemovePaymentLine(PaymentLineViewModel line)
    {
        if (PaymentLines.Count > 1)
        {
            PaymentLines.Remove(line);
        }

        RecalculateRemaining();
    }

    public void RecalculateRemaining() => RemainingAmount = GrandTotal - PaymentLines.Sum(p => p.Amount);

    [RelayCommand]
    private async Task CompleteSaleAsync()
    {
        ErrorMessage = null;

        if (PaymentLines.Any(p => p.Method == PaymentMethod.CustomerCredit) && _owner.SelectedCustomer is null)
        {
            ErrorMessage = "Select a customer to use Customer Credit / Udhaar.";
            return;
        }

        IsSaving = true;
        try
        {
            var request = new CompleteSaleRequest
            {
                Lines = _owner.BuildSaleLineInputs(),
                BillDiscountPercent = _owner.BillDiscountPercent,
                CustomerId = _owner.SelectedCustomer?.Id,
                CashierUserId = _owner.CashierUserId,
                DiscountAuthorizedByUserId = _owner.DiscountAuthorizedByUserId,
                PriceOverrideAuthorizedByUserId = _owner.PriceOverrideAuthorizedByUserId,
                Payments = PaymentLines.Select(p => new SalePaymentInput
                {
                    Method = p.Method,
                    Amount = p.Amount,
                    ReferenceNumber = p.ReferenceNumber,
                    AmountTendered = p.Method == PaymentMethod.Cash ? p.AmountTendered : null,
                }).ToList(),
            };

            CompletedSale = await _saleService.CompleteSaleAsync(request);
        }
        catch (Exception ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsSaving = false;
        }
    }
}
