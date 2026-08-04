using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>One tender in a (possibly split) payment (PRD §20).</summary>
public sealed partial class PaymentLineViewModel : ObservableObject
{
    /// <summary>Exposed per-line (rather than once on the dialog) purely so the ComboBox inside
    /// this row's DataTemplate can bind to it with x:Bind — reaching a dialog-level property from
    /// inside a nested DataTemplate isn't reliable with this project's WinUI 3 tooling.</summary>
    public IReadOnlyList<PaymentMethod> AvailableMethods { get; } = Enum.GetValues<PaymentMethod>();

    [ObservableProperty]
    private PaymentMethod _method = PaymentMethod.Cash;

    [ObservableProperty]
    private string _amountText = "0";

    [ObservableProperty]
    private string? _referenceNumber;

    [ObservableProperty]
    private string? _amountTenderedText;

    public decimal Amount => decimal.TryParse(AmountText, out var v) ? v : 0;

    public decimal? AmountTendered => decimal.TryParse(AmountTenderedText, out var v) ? v : null;

    public decimal? ChangeGiven => Method == PaymentMethod.Cash && AmountTendered is { } t ? t - Amount : null;
}
