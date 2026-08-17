using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

public enum CashRegisterStatus
{
    Open,
    Closed,
}

/// <summary>
/// One opening-to-closing lifecycle for the store's main physical cash drawer. Sales, refunds and
/// credit repayments are deliberately not copied into this row while it is open; reports derive
/// them from their authoritative transaction tables. Closing stores an immutable Z-report snapshot.
/// </summary>
public sealed class CashRegisterSession : Entity
{
    public int StoreId { get; set; }
    public Store Store { get; set; } = null!;

    public string RegisterName { get; set; } = "Main Register";
    public DateTime BusinessDate { get; set; }
    public CashRegisterStatus Status { get; set; } = CashRegisterStatus.Open;

    public int OpenedByUserId { get; set; }
    public User OpenedByUser { get; set; } = null!;
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public decimal OpeningCash { get; set; }

    public int? ClosedByUserId { get; set; }
    public User? ClosedByUser { get; set; }
    public DateTime? ClosedAtUtc { get; set; }

    // Immutable Z-report snapshot, populated only when the session closes.
    public decimal TotalSales { get; set; }
    public int BillCount { get; set; }
    public decimal CashSales { get; set; }
    public decimal UpiSales { get; set; }
    public decimal CardSales { get; set; }
    public decimal CustomerCreditSales { get; set; }
    public decimal TotalReturns { get; set; }
    public decimal CashRefunds { get; set; }
    public decimal CashCreditRepayments { get; set; }
    public decimal CashIn { get; set; }
    public decimal CashOut { get; set; }

    /// <summary>
    /// Physical cash paid out to suppliers during this session. Kept separate from
    /// <see cref="CashOut"/> so the drawer reconciliation shows why the money left, and because
    /// the two come from different sources — supplier payments are derived from SupplierPayments,
    /// CashOut from manually recorded CashMovements.
    /// </summary>
    public decimal SupplierCashPayments { get; set; }

    /// <summary>
    /// Physical cash spent on store expenses during this session (Phase 16A-1). Derived from
    /// Expenses paid with <see cref="PaymentMethod.Cash"/>, never mirrored into a CashMovement —
    /// the expense row is the single financial record, exactly as for
    /// <see cref="SupplierCashPayments"/>.
    ///
    /// <para>Session membership uses <c>Expense.CreatedAtUtc</c>, not <c>ExpenseDateUtc</c>:
    /// the latter is a user-editable ACCOUNTING date that may be backdated to last week, while
    /// the former is when the record — and therefore the cash — actually left the drawer.</para>
    ///
    /// <para>Sessions closed before this column existed hold 0. That is not "no cash was spent";
    /// it means the concept was not part of the snapshot then, and their frozen ExpectedCash /
    /// Variance are deliberately left as the human who counted the drawer signed them off.</para>
    /// </summary>
    public decimal CashExpenses { get; set; }

    public decimal ExpectedCash { get; set; }
    public decimal? ActualCash { get; set; }
    public decimal? Variance { get; set; }

    public ICollection<CashMovement> Movements { get; set; } = new List<CashMovement>();
}
