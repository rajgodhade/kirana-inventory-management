using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>One tender in a (possibly split) payment (PRD §20). Exposes everything the Payment
/// dialog's per-method card needs to decide which of its fields to show — the fields themselves
/// never change, only which subset of them is visible for the currently selected
/// <see cref="Method"/> (PRD §20 redesign: "each payment method displays only the fields that apply
/// to it").</summary>
public sealed partial class PaymentLineViewModel : ObservableObject
{
    /// <summary>Exposed per-line (rather than once on the dialog) purely so the ComboBox inside
    /// this row's DataTemplate can bind to it with x:Bind — reaching a dialog-level property from
    /// inside a nested DataTemplate isn't reliable with this project's WinUI 3 tooling.</summary>
    public IReadOnlyList<PaymentMethod> AvailableMethods { get; } = Enum.GetValues<PaymentMethod>();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ChangeGiven))]
    [NotifyPropertyChangedFor(nameof(IsUnderpaid))]
    [NotifyPropertyChangedFor(nameof(ShowChange))]
    [NotifyPropertyChangedFor(nameof(ShowNormalChange))]
    [NotifyPropertyChangedFor(nameof(ShortfallAmount))]
    [NotifyPropertyChangedFor(nameof(IsCash))]
    [NotifyPropertyChangedFor(nameof(IsUpiOrCard))]
    [NotifyPropertyChangedFor(nameof(IsCustomerCredit))]
    [NotifyPropertyChangedFor(nameof(AmountFieldLabel))]
    [NotifyPropertyChangedFor(nameof(RequiresTransactionId))]
    [NotifyPropertyChangedFor(nameof(HasMissingTransactionId))]
    private PaymentMethod _method = PaymentMethod.Cash;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Amount))]
    [NotifyPropertyChangedFor(nameof(ChangeGiven))]
    [NotifyPropertyChangedFor(nameof(IsUnderpaid))]
    [NotifyPropertyChangedFor(nameof(ShowNormalChange))]
    [NotifyPropertyChangedFor(nameof(ShortfallAmount))]
    [NotifyPropertyChangedFor(nameof(OutstandingAfterSale))]
    private string _amountText = "0";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMissingTransactionId))]
    private string? _referenceNumber;

    /// <summary>True for exactly one line at a time — the last one — whose Amount is computed
    /// automatically as "whatever the other lines don't cover" rather than typed. This is what lets
    /// a cashier split a bill by editing only the line(s) they actually care about (e.g. "₹400
    /// cash") without also having to work out and type the remainder for Udhaar themselves.</summary>
    [ObservableProperty]
    private bool _isAmountLocked;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountTendered))]
    [NotifyPropertyChangedFor(nameof(ChangeGiven))]
    [NotifyPropertyChangedFor(nameof(IsUnderpaid))]
    [NotifyPropertyChangedFor(nameof(ShowChange))]
    [NotifyPropertyChangedFor(nameof(ShowNormalChange))]
    [NotifyPropertyChangedFor(nameof(ShortfallAmount))]
    private string? _amountTenderedText;

    /// <summary>The selected customer's Udhaar balance <em>before</em> this sale — set once by
    /// <see cref="PaymentViewModel"/> when the line is created (the customer can't change mid-dialog),
    /// purely for the Customer Credit card's "Current Outstanding → Outstanding After Sale" display.
    /// Meaningless (and not shown) for any other method.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OutstandingAfterSale))]
    private decimal _currentOutstandingBalance;

    public decimal Amount => decimal.TryParse(AmountText, out var v) ? v : 0;

    public decimal? AmountTendered => decimal.TryParse(AmountTenderedText, out var v) ? v : null;

    public decimal? ChangeGiven => Method == PaymentMethod.Cash && AmountTendered is { } t ? t - Amount : null;

    /// <summary>True when cash tendered on this line is less than what it's covering — physically
    /// impossible ("negative change") and must block completing the sale, not just display oddly
    /// on the printed invoice.</summary>
    public bool IsUnderpaid => ChangeGiven is { } c && c < 0;

    /// <summary>The on-screen Change row only makes sense once tendered is actually entered — a
    /// freshly-added split line with no tendered value yet shouldn't show "Change: ₹0.00".</summary>
    public bool ShowChange => Method == PaymentMethod.Cash && AmountTendered is not null;

    public bool ShowNormalChange => ShowChange && !IsUnderpaid;

    /// <summary>Shown as a positive shortfall ("Short by: ₹58.00") rather than the raw negative
    /// ChangeGiven ("-₹58.00"), which reads as a typo rather than an error.</summary>
    public decimal ShortfallAmount => ChangeGiven is { } c && c < 0 ? -c : 0;

    // ---------------------------------------------------------------- per-method card visibility

    public bool IsCash => Method == PaymentMethod.Cash;
    public bool IsUpiOrCard => Method is PaymentMethod.Upi or PaymentMethod.Card;
    public bool IsCustomerCredit => Method == PaymentMethod.CustomerCredit;

    /// <summary>"Bill amount" was confusing next to a second, near-identical-looking box — this is
    /// what a cashier actually calls the figure, and it changes to "Credit Amount" for Udhaar since
    /// that is what is actually being recorded there, not a payment received on the spot.</summary>
    public string AmountFieldLabel => IsCustomerCredit ? "Credit Amount" : "Payment Amount";

    public bool RequiresTransactionId => IsUpiOrCard;

    public bool HasMissingTransactionId => RequiresTransactionId && string.IsNullOrWhiteSpace(ReferenceNumber);

    /// <summary>"Current Outstanding + This Credit = Outstanding After Sale" — updates live as the
    /// cashier edits the credit amount, using the balance snapshotted when this line was created.</summary>
    public decimal OutstandingAfterSale => CurrentOutstandingBalance + Amount;
}
