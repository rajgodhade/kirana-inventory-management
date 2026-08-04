using CommunityToolkit.Mvvm.ComponentModel;

namespace Kirana.App.ViewModels;

/// <summary>One row in the Customers list. Flattened for XAML binding — no domain entity is bound
/// directly, matching <see cref="SupplierRowViewModel"/>.</summary>
public sealed partial class CustomerRowViewModel : ObservableObject
{
    public int Id { get; set; }

    public string CustomerCode { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Phone { get; set; }

    public string? Address { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingDisplay))]
    [NotifyPropertyChangedFor(nameof(HasOutstanding))]
    private decimal _creditBalance;

    [ObservableProperty]
    private bool _isActive;

    public string OutstandingDisplay => CreditBalance.ToString("0.00");

    public bool HasOutstanding => CreditBalance > 0;
}
