using Kirana.Domain.Entities;

namespace Kirana.Application.CashRegisters;

public sealed record OpenRegisterRequest(decimal OpeningCash, int PerformedByUserId, string RegisterName = "Main Register");

public sealed record RecordCashMovementRequest(
    CashMovementType Type,
    decimal Amount,
    string Reason,
    int PerformedByUserId,
    Guid OperationId);

public sealed record CloseRegisterRequest(decimal ActualCash, int PerformedByUserId);

public sealed class CashRegisterReport
{
    public int SessionId { get; init; }
    public string StoreName { get; init; } = string.Empty;
    public string RegisterName { get; init; } = string.Empty;
    public DateTime BusinessDate { get; init; }
    public CashRegisterStatus Status { get; init; }
    public string OpenedBy { get; init; } = string.Empty;
    public DateTime OpenedAtUtc { get; init; }
    public string? ClosedBy { get; init; }
    public DateTime? ClosedAtUtc { get; init; }
    public decimal OpeningCash { get; init; }
    public decimal TotalSales { get; init; }
    public int BillCount { get; init; }
    public decimal CashSales { get; init; }
    public decimal UpiSales { get; init; }
    public decimal CardSales { get; init; }
    public decimal CustomerCreditSales { get; init; }
    public decimal TotalReturns { get; init; }
    public decimal CashRefunds { get; init; }
    public decimal CashCreditRepayments { get; init; }
    public decimal CashIn { get; init; }
    public decimal CashOut { get; init; }
    public decimal SupplierCashPayments { get; init; }

    /// <summary>Cash spent on store expenses in this session (Phase 16A-1). Live for an open
    /// session, frozen from the snapshot once closed. Sessions closed before the concept existed
    /// report 0.</summary>
    public decimal CashExpenses { get; init; }

    public decimal ExpectedCash { get; init; }
    public decimal? ActualCash { get; init; }
    public decimal? Variance { get; init; }
    public IReadOnlyList<CashMovementRow> Movements { get; init; } = [];
}

/// <summary>
/// What a drawer movement line in a register report represents. Wider than the persisted
/// <see cref="CashMovementType"/>: supplier payments and cash expenses are not stored as
/// CashMovement rows, they are derived from SupplierPayments and Expenses, so this is a reporting
/// concept rather than a storage one.
/// </summary>
public enum CashRegisterMovementKind
{
    CashIn,
    CashOut,
    SupplierPayment,
    Expense,
}

public sealed record CashMovementRow(
    int Id, CashRegisterMovementKind Type, decimal Amount, string Reason, string PerformedBy, DateTime OccurredAtUtc);

public sealed record CashRegisterHistoryRow(
    int Id, DateTime BusinessDate, string RegisterName, string OpenedBy, string? ClosedBy,
    DateTime OpenedAtUtc, DateTime? ClosedAtUtc, decimal OpeningCash, decimal ExpectedCash,
    decimal? ActualCash, decimal? Variance, CashRegisterStatus Status);

public sealed record CashRegisterStatusSummary(
    bool IsOpen, int? SessionId, string RegisterName, string? OpenedBy, DateTime? OpenedAtUtc,
    decimal OpeningCash, decimal ExpectedCash);
