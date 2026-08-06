using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Kirana.Application.Billing;
using Kirana.Application.Printing;
using Kirana.Domain.Entities;

namespace Kirana.App.ViewModels;

/// <summary>Backs the Payment dialog (PRD §20) — Cash/UPI/Card/Customer Credit, with split
/// payment support. Completing here <em>is</em> completing the sale: once the payment lines
/// sum to the grand total, "Complete Sale" calls <see cref="ISaleService.CompleteSaleAsync"/>.
///
/// This is a UI/UX-only surface over unchanged business rules: every guard here either mirrors a
/// rule <see cref="SaleService"/> already enforces server-side (amounts must sum to the total, cash
/// tendered can't be less than what it covers) or is a purely cosmetic dialog-side convenience
/// (missing Transaction ID, duplicate payment method) that blocks nothing SaleService itself would
/// accept — it just stops an obviously-wrong split from ever reaching the server.</summary>
public sealed partial class PaymentViewModel : ObservableObject
{
    /// <summary>The four cash denominations a cashier reaches for most often, per PRD's quick-tender
    /// request. "Exact Amount" is represented as a null entry (see <see cref="QuickTenderOption"/>).</summary>
    public static readonly IReadOnlyList<decimal> QuickTenderDenominations = [100m, 200m, 500m, 1000m];

    private readonly PosShellViewModel _owner;
    private readonly ISaleService _saleService;

    public decimal GrandTotal { get; }
    public IReadOnlyList<PaymentMethod> AvailableMethods { get; } = Enum.GetValues<PaymentMethod>();

    /// <summary>For the dialog's title — makes "Payment" specific to who this sale is for, instead
    /// of a bare generic label.</summary>
    public string CustomerDisplayText => _owner.CustomerDisplayText;

    public ObservableCollection<PaymentLineViewModel> PaymentLines { get; } = [];

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBalanced))]
    [NotifyPropertyChangedFor(nameof(IsOverAllocated))]
    [NotifyPropertyChangedFor(nameof(OverAllocatedAmount))]
    [NotifyPropertyChangedFor(nameof(IsReadyToComplete))]
    [NotifyPropertyChangedFor(nameof(ValidationMessage))]
    private decimal _remainingAmount;

    /// <summary>The normal, expected state: every rupee of the bill is accounted for. One of several
    /// conditions <see cref="IsReadyToComplete"/> folds together to gate "Complete Sale".</summary>
    public bool IsBalanced => Math.Abs(RemainingAmount) < 0.005m;

    /// <summary>True only when the lines the cashier typed (everything except the auto-balancing
    /// last line) already add up to more than the bill by themselves — the one case the
    /// auto-balancing last line can't paper over, since it can't go negative.</summary>
    public bool IsOverAllocated => RemainingAmount < -0.005m;

    public decimal OverAllocatedAmount => IsOverAllocated ? -RemainingAmount : 0;

    // ---------------------------------------------------------------- live payment summary

    /// <summary>
    /// The exact rows the receipt will print, built live from the same
    /// <see cref="PaymentSummaryBuilder"/> the invoice uses — not a parallel calculation. A generic
    /// "Paid" total was actively misleading here: it summed each line's <em>Amount</em>, so a cash
    /// line claiming to cover ₹250 while only ₹200 was actually handed over displayed "Paid ₹250".
    /// Showing the real per-method breakdown ("Cash Paid ₹200", "Customer Credit ₹50") instead means
    /// the dialog and the printed invoice cannot disagree, because they are literally the same code.
    /// </summary>
    public IReadOnlyList<InvoicePaymentSummaryLine> PaymentSummaryLines =>
        PaymentSummaryBuilder.Build(PaymentLines.Select(ToInvoicePaymentLine).ToList());

    /// <summary>Mirrors exactly what <see cref="CompleteSaleAsync"/> sends to
    /// <c>SaleService</c> — including how it derives ChangeGiven — so the preview can never differ
    /// from what actually gets persisted and printed.</summary>
    private static InvoicePaymentLine ToInvoicePaymentLine(PaymentLineViewModel line) => new()
    {
        Method = line.Method,
        Amount = line.Amount,
        ReferenceNumber = line.ReferenceNumber,
        AmountTendered = line.Method == PaymentMethod.Cash ? line.AmountTendered : null,
        ChangeGiven = line.Method == PaymentMethod.Cash && line.AmountTendered is { } tendered
            ? tendered - line.Amount
            : null,
    };

    public bool HasDuplicateMethod => PaymentLines.GroupBy(p => p.Method).Any(g => g.Count() > 1);

    public bool HasMissingTransactionId => PaymentLines.Any(p => p.HasMissingTransactionId);

    public bool HasUnderpaidLine => PaymentLines.Any(p => p.IsUnderpaid);

    /// <summary>Every condition — server-enforced or dialog-only convenience — that must hold before
    /// "Complete Sale" is even clickable. Drives <c>IsPrimaryButtonEnabled</c> directly, so an invalid
    /// split can never reach <see cref="CompleteSaleAsync"/> at all, not just get rejected by it.</summary>
    public bool IsReadyToComplete =>
        IsBalanced && !IsOverAllocated && !HasUnderpaidLine && !HasDuplicateMethod && !HasMissingTransactionId;

    /// <summary>One human-readable line summarizing exactly why "Complete Sale" is (or isn't) enabled
    /// right now — checked in the same priority order a cashier would want to fix them in.</summary>
    public string ValidationMessage =>
        HasUnderpaidLine ? "Cash received is less than the amount due on that line." :
        IsOverAllocated ? $"Overpayment — amounts add up to ₹{OverAllocatedAmount:0.00} more than the total." :
        !IsBalanced ? "Payment Incomplete" :
        HasDuplicateMethod ? "Duplicate payment method — combine into a single line instead." :
        HasMissingTransactionId ? "Enter a Transaction ID for each UPI/Card payment." :
        "Ready to Complete Sale";

    public Sale? CompletedSale { get; private set; }

    private bool _isRebalancing;

    public PaymentViewModel(PosShellViewModel owner, ISaleService saleService)
    {
        _owner = owner;
        _saleService = saleService;
        GrandTotal = owner.GrandTotal;

        // Cash tendered is deliberately left blank rather than defaulted to the bill total: a
        // customer paying part-cash/part-Udhaar is NOT handing over the full total and getting
        // change back on it — they're handing over exactly the cash line's own amount. Prefilling
        // tendered here previously left it stuck at a stale, too-large figure once a split reduced
        // the cash line's own Amount, producing a printed invoice that showed both "change
        // returned" and an Udhaar debt for the same rupees. Leaving it blank means no Change is
        // shown at all (see PaymentLineViewModel.ShowChange) until the cashier actually types what
        // was handed over — exact payment is the default assumption, not a large-note-with-change.
        PaymentLines.Add(CreatePaymentLine(GrandTotal.ToString("0.00")));
        RecalculateRemaining();
    }

    private PaymentLineViewModel CreatePaymentLine(string amountText = "0") => new()
    {
        AmountText = amountText,
        // Snapshotted once per line: the customer can't change mid-dialog, and this is purely
        // display data for the Customer Credit card's running "outstanding after sale" math.
        CurrentOutstandingBalance = _owner.SelectedCustomer?.CreditBalance ?? 0,
    };

    [RelayCommand]
    private void AddPaymentLine()
    {
        // No need to pre-fill an amount here — RecalculateRemaining below works out what this new
        // (now-last) line should auto-cover and fills it in.
        PaymentLines.Add(CreatePaymentLine());
        RecalculateRemaining();

        // Then settle any shortfall the cashier had already entered before adding this method.
        // This is the common real-world order — "customer's only got ₹200 of the ₹250" gets typed
        // into Cash Received first, and only *then* does the cashier reach for Udhaar. Until this
        // ran here, that shortfall stayed stranded on the cash line: the new line computed ₹0, the
        // summary still claimed the full amount was paid, and Complete Sale stayed disabled behind
        // a "cash received is less than the amount due" warning with no obvious way out.
        SettleCashShortfalls();
    }

    public void RemovePaymentLine(PaymentLineViewModel line)
    {
        if (PaymentLines.Count > 1)
        {
            PaymentLines.Remove(line);
        }

        RecalculateRemaining();
        SettleCashShortfalls();
    }

    /// <summary>Applies a quick-tender denomination (or the exact amount due) to one Cash line's
    /// "Cash Received" field — the one-click alternative to typing it, per PRD's cash-convenience
    /// request. <paramref name="amount"/> of <c>null</c> means "Exact Amount": tendered is set to
    /// exactly what that line's own Amount is, giving ₹0 change.</summary>
    public void ApplyQuickTender(PaymentLineViewModel line, decimal? amount)
    {
        line.AmountTenderedText = (amount ?? line.Amount).ToString("0.00");

        // A quick-tender click is one atomic edit, not a stream of keystrokes — safe to settle any
        // resulting shortfall immediately rather than waiting for the dialog's debounce timer.
        RecalculateRemaining();
        SettleCashShortfalls();
    }

    /// <summary>
    /// With two or more lines, keeps the last one locked to "whatever the total isn't otherwise
    /// covering," recalculated after every edit. This is what turns a split payment from "type two
    /// numbers that must add up exactly, with no help" into "type the amount(s) you actually know,
    /// and the last method absorbs the rest automatically." A cashier splitting ₹549 as ₹400 cash +
    /// Udhaar now only ever types "400" — the ₹149 Udhaar line fills itself in and stays correct as
    /// the cash figure changes. With only one line there is nothing else for it to "absorb" — it
    /// stays a normal editable field defaulting to the full total, same as before split payments
    /// existed at all.
    /// </summary>
    public void RecalculateRemaining()
    {
        if (_isRebalancing)
        {
            return;
        }

        _isRebalancing = true;
        try
        {
            for (var i = 0; i < PaymentLines.Count; i++)
            {
                PaymentLines[i].IsAmountLocked = PaymentLines.Count > 1 && i == PaymentLines.Count - 1;
            }

            if (PaymentLines.Count > 1)
            {
                var last = PaymentLines[^1];
                var othersTotal = PaymentLines.Take(PaymentLines.Count - 1).Sum(p => p.Amount);

                // Clamped at zero: if the cashier's own entries already exceed the bill, the last
                // line has nothing left to cover — it must never go negative to "absorb" an
                // overpayment, that's what IsOverAllocated below is for instead.
                var autoAmountText = Math.Max(0, GrandTotal - othersTotal).ToString("0.00");
                if (last.AmountText != autoAmountText)
                {
                    last.AmountText = autoAmountText;
                }
            }

            RemainingAmount = GrandTotal - PaymentLines.Sum(p => p.Amount);
            NotifySummaryChanged();
        }
        finally
        {
            _isRebalancing = false;
        }
    }

    /// <summary>
    /// If the cashier types less into "Cash Received" than a (non-last) line's own Payment Amount,
    /// that shortfall shouldn't just sit there as a blocking "Short by" error — the auto-balancing
    /// last line already exists to absorb exactly this. Snaps that line's own Amount down to what
    /// was actually tendered, so the difference flows into the last line (Udhaar, most often) on
    /// the immediately following <see cref="RecalculateRemaining"/>.
    ///
    /// Deliberately called from a debounce timer on the dialog side (<c>PaymentDialog</c>'s
    /// 300ms Tick), never straight from a keystroke: comparing tendered against Amount live on
    /// every character would misfire mid-type — typing "100" fires this once for "1" (snaps Amount
    /// to 1), then never again once the partial value "10"/"100" is no longer less than that
    /// already-shrunken Amount, permanently corrupting the split before the cashier finishes
    /// typing. Settling only once the cashier actually pauses avoids that entirely.
    ///
    /// One-directional by design: it only ever pulls Amount down to meet a real shortfall, never
    /// back up — if the cashier corrects Cash Received upward afterwards, Payment Amount (still
    /// freely editable, not locked) is theirs to raise again too.
    /// </summary>
    public void SettleCashShortfalls()
    {
        var changed = false;

        for (var i = 0; i < PaymentLines.Count - 1; i++)
        {
            var line = PaymentLines[i];
            if (line.Method == PaymentMethod.Cash && line.AmountTendered is { } tendered && tendered < line.Amount)
            {
                line.AmountText = tendered.ToString("0.00");
                changed = true;
            }
        }

        if (changed)
        {
            RecalculateRemaining();
        }
    }

    /// <summary>The live summary panel (Paid/Credit/Change/validation) depends on every line's
    /// Amount/Method/ReferenceNumber at once, none of which individually maps to one of these
    /// dialog-level properties via <c>[NotifyPropertyChangedFor]</c> — so they're recomputed and
    /// re-announced together, right after <see cref="RemainingAmount"/> itself, from the one place
    /// that already runs after every relevant edit.</summary>
    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(PaymentSummaryLines));
        OnPropertyChanged(nameof(HasDuplicateMethod));
        OnPropertyChanged(nameof(HasMissingTransactionId));
        OnPropertyChanged(nameof(HasUnderpaidLine));
        OnPropertyChanged(nameof(IsReadyToComplete));
        OnPropertyChanged(nameof(ValidationMessage));
    }

    [RelayCommand]
    private async Task CompleteSaleAsync()
    {
        ErrorMessage = null;

        if (IsOverAllocated)
        {
            ErrorMessage = $"These amounts add up to ₹{OverAllocatedAmount:0.00} more than the bill. Reduce one of them.";
            return;
        }

        // Fail fast client-side rather than letting an obviously-impossible payment (cash tendered
        // less than the amount it's covering — a negative "change returned") reach the server and
        // print on the invoice. SaleService re-validates this independently too.
        var underpaidLine = PaymentLines.FirstOrDefault(p => p.IsUnderpaid);
        if (underpaidLine is not null)
        {
            ErrorMessage =
                $"Cash received (₹{underpaidLine.AmountTendered:0.00}) is less than the amount due " +
                $"(₹{underpaidLine.Amount:0.00}) for this payment line.";
            return;
        }

        // Dialog-side conveniences only — SaleService has no rule against either of these, but
        // there is no legitimate reason for a real sale to need them, so they're stopped here
        // rather than left to produce a confusing invoice.
        if (HasDuplicateMethod)
        {
            ErrorMessage = "Two payment lines use the same method — combine them into one line instead.";
            return;
        }

        if (HasMissingTransactionId)
        {
            ErrorMessage = "Enter a Transaction ID for each UPI/Card payment before completing the sale.";
            return;
        }

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
