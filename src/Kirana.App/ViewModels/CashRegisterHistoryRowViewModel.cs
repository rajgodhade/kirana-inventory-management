using Kirana.Application.CashRegisters;

namespace Kirana.App.ViewModels;

public sealed class CashRegisterHistoryRowViewModel(CashRegisterHistoryRow row)
{
    public int Id => row.Id;
    public bool IsClosed => row.Status == Kirana.Domain.Entities.CashRegisterStatus.Closed;
    public string BusinessDate => row.BusinessDate.ToString("dd MMM yyyy");
    public string RegisterName => row.RegisterName;
    public string OpenedBy => row.OpenedBy;
    public string ClosedBy => row.ClosedBy ?? "—";
    public string OpenedAt => row.OpenedAtUtc.ToLocalTime().ToString("dd MMM, hh:mm tt");
    public string ClosedAt => row.ClosedAtUtc?.ToLocalTime().ToString("dd MMM, hh:mm tt") ?? "—";
    public string Expected => CashRegisterViewModel.Currency(row.ExpectedCash);
    public string Actual => row.ActualCash is null ? "—" : CashRegisterViewModel.Currency(row.ActualCash.Value);
    public string Variance => row.Variance is null ? "—" : CashRegisterViewModel.Currency(row.Variance.Value);
    public string Status => row.Status.ToString();
}
