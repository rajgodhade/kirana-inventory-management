using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Authentication;
using Kirana.Application.CashRegisters;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

public sealed partial class CashRegisterViewModel(
    ICashRegisterService registerService,
    ManagementSession session) : ObservableObject
{
    private static readonly CultureInfo Inr = CultureInfo.GetCultureInfo("en-IN");

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isOpen;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _statusMessage;
    [ObservableProperty] private int? _sessionId;
    [ObservableProperty] private string _registerName = "Main Register";
    [ObservableProperty] private string _openedBy = "—";
    [ObservableProperty] private string _openedAt = "—";
    [ObservableProperty] private decimal _openingCash;
    [ObservableProperty] private decimal _cashSales;
    [ObservableProperty] private decimal _cashRefunds;
    [ObservableProperty] private decimal _cashCreditRepayments;
    [ObservableProperty] private decimal _cashIn;
    [ObservableProperty] private decimal _cashOut;
    [ObservableProperty] private decimal _supplierCashPayments;
    [ObservableProperty] private decimal _expectedCash;

    public ObservableCollection<CashRegisterHistoryRowViewModel> History { get; } = [];
    public int CurrentUserId => session.CurrentUser?.Id ?? throw new InvalidOperationException("Management session is locked.");
    public bool CanOpenClose => session.HasPermission(PermissionKeys.CashRegisterOpenClose);
    public bool CanCashIn => session.HasPermission(PermissionKeys.CashRegisterCashIn);
    public bool CanCashOut => session.HasPermission(PermissionKeys.CashRegisterCashOut);
    public string StatusLabel => IsOpen ? "OPEN" : "CLOSED";
    public string OpeningCashText => Currency(OpeningCash);
    public string CashSalesText => Currency(CashSales);
    public string CashRefundsText => Currency(CashRefunds);
    public string CashRepaymentsText => Currency(CashCreditRepayments);
    public string CashInText => Currency(CashIn);
    public string CashOutText => Currency(CashOut);
    public string SupplierCashPaymentsText => Currency(SupplierCashPayments);
    public string ExpectedCashText => Currency(ExpectedCash);

    public async Task LoadAsync()
    {
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var status = await registerService.GetStatusAsync();
            IsOpen = status.IsOpen;
            SessionId = status.SessionId;
            RegisterName = status.RegisterName;
            OpenedBy = status.OpenedBy ?? "—";
            OpenedAt = status.OpenedAtUtc?.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt") ?? "—";
            OpeningCash = status.OpeningCash;
            ExpectedCash = status.ExpectedCash;

            if (status.IsOpen)
            {
                var report = await registerService.GetCurrentReportAsync(CurrentUserId);
                Apply(report);
            }
            else
            {
                CashSales = CashRefunds = CashCreditRepayments = CashIn = CashOut = SupplierCashPayments = 0m;
            }

            History.Clear();
            foreach (var row in await registerService.GetHistoryAsync(CurrentUserId)) History.Add(new(row));
        }
        catch (Exception ex) { ErrorMessage = ex.Message; }
        finally { IsBusy = false; }
    }

    public async Task<bool> OpenAsync(decimal amount)
    {
        return await ExecuteAsync(async () =>
        {
            await registerService.OpenAsync(new OpenRegisterRequest(amount, CurrentUserId));
            StatusMessage = $"Register opened with {Currency(amount)}.";
        });
    }

    public async Task<bool> RecordMovementAsync(CashMovementType type, decimal amount, string reason, Guid operationId)
    {
        return await ExecuteAsync(async () =>
        {
            await registerService.RecordMovementAsync(new(type, amount, reason, CurrentUserId, operationId));
            StatusMessage = $"{(type == CashMovementType.CashIn ? "Cash In" : "Cash Out")} of {Currency(amount)} recorded.";
        });
    }

    public Task<CashRegisterReport> GetXReportAsync(decimal? counted = null) =>
        registerService.GetXReportAsync(CurrentUserId, counted);

    public async Task<CashRegisterReport?> CloseAsync(decimal actualCash)
    {
        CashRegisterReport? result = null;
        var success = await ExecuteAsync(async () =>
        {
            result = await registerService.CloseAsync(new(actualCash, CurrentUserId));
            StatusMessage = "Register closed and Z Report saved.";
        });
        return success ? result : null;
    }

    public Task<CashRegisterReport> GetZReportAsync(int id) => registerService.GetZReportAsync(id, CurrentUserId);

    private async Task<bool> ExecuteAsync(Func<Task> action)
    {
        IsBusy = true; ErrorMessage = null; StatusMessage = null;
        try { await action(); await LoadAsync(); return true; }
        catch (Exception ex) { ErrorMessage = ex.Message; return false; }
        finally { IsBusy = false; }
    }

    private void Apply(CashRegisterReport report)
    {
        SessionId = report.SessionId; IsOpen = report.Status == CashRegisterStatus.Open;
        RegisterName = report.RegisterName; OpenedBy = report.OpenedBy;
        OpenedAt = report.OpenedAtUtc.ToLocalTime().ToString("dd MMM yyyy, hh:mm tt");
        OpeningCash = report.OpeningCash; CashSales = report.CashSales; CashRefunds = report.CashRefunds;
        CashCreditRepayments = report.CashCreditRepayments; CashIn = report.CashIn;
        CashOut = report.CashOut; SupplierCashPayments = report.SupplierCashPayments;
        ExpectedCash = report.ExpectedCash;
    }

    partial void OnIsOpenChanged(bool value) => OnPropertyChanged(nameof(StatusLabel));
    partial void OnOpeningCashChanged(decimal value) => OnPropertyChanged(nameof(OpeningCashText));
    partial void OnCashSalesChanged(decimal value) => OnPropertyChanged(nameof(CashSalesText));
    partial void OnCashRefundsChanged(decimal value) => OnPropertyChanged(nameof(CashRefundsText));
    partial void OnCashCreditRepaymentsChanged(decimal value) => OnPropertyChanged(nameof(CashRepaymentsText));
    partial void OnCashInChanged(decimal value) => OnPropertyChanged(nameof(CashInText));
    partial void OnCashOutChanged(decimal value) => OnPropertyChanged(nameof(CashOutText));
    partial void OnSupplierCashPaymentsChanged(decimal value) => OnPropertyChanged(nameof(SupplierCashPaymentsText));
    partial void OnExpectedCashChanged(decimal value) => OnPropertyChanged(nameof(ExpectedCashText));

    public static string Currency(decimal value)
    {
        var rounded = Math.Round(value, 2, MidpointRounding.AwayFromZero);
        if (rounded == 0m) rounded = 0m;

        var prefix = rounded < 0m ? "-₹" : "₹";
        return prefix + Math.Abs(rounded).ToString("N2", Inr);
    }
}
