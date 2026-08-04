using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the "Resume held bill" dialog (PRD §20, F7).</summary>
public sealed partial class HeldBillsViewModel : ObservableObject
{
    private readonly PosShellViewModel _owner;

    public ObservableCollection<HeldBill> HeldBills { get; } = [];

    [ObservableProperty]
    private string? _errorMessage;

    public int? ResumedHeldBillId { get; private set; }

    public HeldBillsViewModel(PosShellViewModel owner)
    {
        _owner = owner;
    }

    public async Task LoadAsync()
    {
        var bills = await _owner.GetHeldBillsAsync();
        HeldBills.Clear();
        foreach (var bill in bills)
        {
            HeldBills.Add(bill);
        }
    }

    public void SelectForResume(HeldBill bill) => ResumedHeldBillId = bill.Id;
}
