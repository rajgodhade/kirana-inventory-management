using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.CashRegisters;

public sealed class CashRegisterService(
    IKiranaDbContext db,
    IPermissionEnforcer permissions,
    IAuditLogger auditLogger) : ICashRegisterService
{
    public async Task<CashRegisterStatusSummary> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var session = await ActiveQuery().AsNoTracking().FirstOrDefaultAsync(cancellationToken);
        if (session is null)
            return new(false, null, "Main Register", null, null, 0m, 0m);

        var report = await CalculateAsync(session, DateTime.UtcNow, null, cancellationToken);
        return new(true, session.Id, session.RegisterName, session.OpenedByUser.FullName,
            session.OpenedAtUtc, session.OpeningCash, report.ExpectedCash);
    }

    public async Task<CashRegisterSession> OpenAsync(OpenRegisterRequest request, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.CashRegisterOpenClose, cancellationToken);
        if (request.OpeningCash < 0m) throw new ArgumentException("Opening cash cannot be negative.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.RegisterName)) throw new ArgumentException("Register name is required.", nameof(request));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.CashRegisterSessions.AnyAsync(x => x.Status == CashRegisterStatus.Open, cancellationToken))
            throw new InvalidOperationException("The register is already open.");

        var store = await db.Stores.SingleAsync(cancellationToken);
        var nowUtc = DateTime.UtcNow;
        var session = new CashRegisterSession
        {
            StoreId = store.Id,
            RegisterName = request.RegisterName.Trim(),
            BusinessDate = DateTime.Now.Date,
            OpenedByUserId = request.PerformedByUserId,
            OpenedAtUtc = nowUtc,
            OpeningCash = Money(request.OpeningCash),
        };
        db.CashRegisterSessions.Add(session);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await auditLogger.RecordAsync(request.PerformedByUserId, "CashRegisterOpened", nameof(CashRegisterSession),
                session.Id.ToString(), newValue: $"Opening cash ₹{session.OpeningCash:0.00}; business date {session.BusinessDate:yyyy-MM-dd}",
                cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return session;
        }
        catch (DbUpdateException ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InvalidOperationException("The register could not be opened because another active session exists.", ex);
        }
    }

    public async Task<CashMovement> RecordMovementAsync(RecordCashMovementRequest request, CancellationToken cancellationToken = default)
    {
        var permission = request.Type == CashMovementType.CashIn
            ? PermissionKeys.CashRegisterCashIn : PermissionKeys.CashRegisterCashOut;
        await permissions.EnsureHasPermissionAsync(request.PerformedByUserId, permission, cancellationToken);
        if (request.Amount <= 0m) throw new ArgumentException("Cash movement amount must be greater than zero.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ArgumentException("A reason is required.", nameof(request));
        if (request.OperationId == Guid.Empty) throw new ArgumentException("An operation identifier is required.", nameof(request));

        var existing = await db.CashMovements.AsNoTracking()
            .FirstOrDefaultAsync(x => x.OperationId == request.OperationId, cancellationToken);
        if (existing is not null) return existing;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var session = await db.CashRegisterSessions
            .FirstOrDefaultAsync(x => x.Status == CashRegisterStatus.Open, cancellationToken)
            ?? throw new InvalidOperationException("Open the register before recording a cash movement.");

        if (request.Type == CashMovementType.CashOut)
        {
            // Physical cash can't go negative: a "cash out" only ever represents money that was
            // actually counted out of the drawer, so it can never exceed what the drawer
            // currently holds.
            var drawer = await CalculateCashDrawerFiguresAsync(session, DateTime.UtcNow, cancellationToken);
            if (request.Amount > drawer.ExpectedCash)
            {
                throw new InvalidOperationException(
                    $"Cash out of ₹{request.Amount:0.00} exceeds the ₹{drawer.ExpectedCash:0.00} currently in the drawer.");
            }
        }

        var movement = new CashMovement
        {
            RegisterSessionId = session.Id,
            OperationId = request.OperationId,
            Type = request.Type,
            Amount = Money(request.Amount),
            Reason = request.Reason.Trim(),
            PerformedByUserId = request.PerformedByUserId,
            OccurredAtUtc = DateTime.UtcNow,
        };
        db.CashMovements.Add(movement);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await auditLogger.RecordAsync(request.PerformedByUserId,
                request.Type == CashMovementType.CashIn ? "CashRegisterCashIn" : "CashRegisterCashOut",
                nameof(CashMovement), movement.Id.ToString(),
                newValue: $"₹{movement.Amount:0.00}", reason: movement.Reason, cancellationToken: cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return movement;
        }
        catch (DbUpdateException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return await db.CashMovements.AsNoTracking().SingleAsync(x => x.OperationId == request.OperationId, cancellationToken);
        }
    }

    public async Task<CashRegisterReport> GetXReportAsync(
        int performedByUserId, decimal? countedCashPreview = null, CancellationToken cancellationToken = default)
    {
        var report = await GetCurrentReportAsync(performedByUserId, countedCashPreview, cancellationToken);
        await auditLogger.RecordAsync(performedByUserId, "CashRegisterXReportViewed", nameof(CashRegisterSession),
            report.SessionId.ToString(), cancellationToken: cancellationToken);
        return report;
    }

    public async Task<CashRegisterReport> GetCurrentReportAsync(
        int performedByUserId, decimal? countedCashPreview = null, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CashRegisterView, cancellationToken);
        if (countedCashPreview < 0m) throw new ArgumentException("Counted cash cannot be negative.", nameof(countedCashPreview));
        var session = await ActiveQuery().AsNoTracking().FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("There is no open register.");
        return await CalculateAsync(session, DateTime.UtcNow, countedCashPreview, cancellationToken);
    }

    public async Task<CashRegisterReport> CloseAsync(CloseRegisterRequest request, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.CashRegisterOpenClose, cancellationToken);
        if (request.ActualCash < 0m) throw new ArgumentException("Counted cash cannot be negative.", nameof(request));

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var session = await ActiveQuery().FirstOrDefaultAsync(cancellationToken)
            ?? throw new InvalidOperationException("There is no open register to close.");
        var closedAtUtc = DateTime.UtcNow;
        var report = await CalculateAsync(session, closedAtUtc, request.ActualCash, cancellationToken);

        session.Status = CashRegisterStatus.Closed;
        session.ClosedByUserId = request.PerformedByUserId;
        session.ClosedAtUtc = closedAtUtc;
        session.TotalSales = report.TotalSales;
        session.BillCount = report.BillCount;
        session.CashSales = report.CashSales;
        session.UpiSales = report.UpiSales;
        session.CardSales = report.CardSales;
        session.CustomerCreditSales = report.CustomerCreditSales;
        session.TotalReturns = report.TotalReturns;
        session.CashRefunds = report.CashRefunds;
        session.CashCreditRepayments = report.CashCreditRepayments;
        session.CashIn = report.CashIn;
        session.CashOut = report.CashOut;
        session.SupplierCashPayments = report.SupplierCashPayments;
        session.CashExpenses = report.CashExpenses;
        session.ExpectedCash = report.ExpectedCash;
        session.ActualCash = Money(request.ActualCash);
        session.Variance = Money(session.ActualCash.Value - session.ExpectedCash);
        session.UpdatedAtUtc = closedAtUtc;

        await db.SaveChangesAsync(cancellationToken);
        await auditLogger.RecordAsync(request.PerformedByUserId, "CashRegisterClosed", nameof(CashRegisterSession),
            session.Id.ToString(), newValue: $"Expected ₹{session.ExpectedCash:0.00}; counted ₹{session.ActualCash:0.00}; variance ₹{session.Variance:0.00}",
            cancellationToken: cancellationToken);
        await auditLogger.RecordAsync(request.PerformedByUserId, "CashRegisterZReportGenerated", nameof(CashRegisterSession),
            session.Id.ToString(), cancellationToken: cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return await BuildClosedReportAsync(session.Id, cancellationToken);
    }

    public async Task<IReadOnlyList<CashRegisterHistoryRow>> GetHistoryAsync(
        int performedByUserId, int take = 100, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CashRegisterView, cancellationToken);
        take = Math.Clamp(take, 1, 500);
        var sessions = await db.CashRegisterSessions.AsNoTracking()
            .Include(x => x.Store).Include(x => x.OpenedByUser).Include(x => x.ClosedByUser)
            .OrderByDescending(x => x.OpenedAtUtc).Take(take)
            .ToListAsync(cancellationToken);

        decimal? liveExpectedCash = null;
        var openSession = sessions.FirstOrDefault(x => x.Status == CashRegisterStatus.Open);
        if (openSession is not null)
        {
            liveExpectedCash = (await CalculateAsync(
                openSession, DateTime.UtcNow, null, cancellationToken)).ExpectedCash;
        }

        return sessions.Select(x => new CashRegisterHistoryRow(x.Id, x.BusinessDate, x.RegisterName,
            x.OpenedByUser.FullName, x.ClosedByUser?.FullName,
            x.OpenedAtUtc, x.ClosedAtUtc, x.OpeningCash,
            x.Status == CashRegisterStatus.Open && x.Id == openSession?.Id
                ? liveExpectedCash!.Value
                : x.ExpectedCash,
            x.ActualCash, x.Variance, x.Status)).ToList();
    }

    public async Task<CashRegisterReport> GetZReportAsync(
        int sessionId, int performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissions.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.CashRegisterView, cancellationToken);
        var report = await BuildClosedReportAsync(sessionId, cancellationToken);
        await auditLogger.RecordAsync(performedByUserId, "CashRegisterZReportViewed", nameof(CashRegisterSession),
            sessionId.ToString(), cancellationToken: cancellationToken);
        return report;
    }

    private IQueryable<CashRegisterSession> ActiveQuery() => db.CashRegisterSessions
        .Include(x => x.Store).Include(x => x.OpenedByUser).Include(x => x.ClosedByUser)
        .Where(x => x.Status == CashRegisterStatus.Open);

    private async Task<CashRegisterReport> CalculateAsync(
        CashRegisterSession session, DateTime asOfUtc, decimal? countedCash, CancellationToken cancellationToken)
    {
        var sales = db.Sales.AsNoTracking()
            .Where(x => x.Status == SaleStatus.Completed && x.SaleDateUtc >= session.OpenedAtUtc && x.SaleDateUtc <= asOfUtc);
        var payments = sales.SelectMany(x => x.Payments);
        var returns = db.SalesReturns.AsNoTracking()
            .Where(x => x.ReturnDateUtc >= session.OpenedAtUtc && x.ReturnDateUtc <= asOfUtc);
        var movements = db.CashMovements.AsNoTracking()
            .Where(x => x.RegisterSessionId == session.Id && x.OccurredAtUtc <= asOfUtc);

        var totalSales = Money(await sales.SumAsync(x => (decimal?)x.GrandTotal, cancellationToken) ?? 0m);
        var billCount = await sales.CountAsync(cancellationToken);
        var upiSales = await SumPaymentsAsync(payments, PaymentMethod.Upi, cancellationToken);
        var cardSales = await SumPaymentsAsync(payments, PaymentMethod.Card, cancellationToken);
        var creditSales = await SumPaymentsAsync(payments, PaymentMethod.CustomerCredit, cancellationToken);
        var totalReturns = Money(await returns.SumAsync(x => (decimal?)x.TotalReturnAmount, cancellationToken) ?? 0m);
        var drawer = await CalculateCashDrawerFiguresAsync(session, asOfUtc, cancellationToken);
        var movementRows = await movements.Include(x => x.PerformedByUser).OrderBy(x => x.OccurredAtUtc)
            .Select(x => new CashMovementRow(
                x.Id,
                x.Type == CashMovementType.CashIn ? CashRegisterMovementKind.CashIn : CashRegisterMovementKind.CashOut,
                x.Amount, x.Reason, x.PerformedByUser.FullName, x.OccurredAtUtc))
            .ToListAsync(cancellationToken);
        movementRows.AddRange(await SupplierCashPaymentRowsAsync(session, asOfUtc, cancellationToken));
        movementRows.AddRange(await CashExpenseRowsAsync(session, asOfUtc, cancellationToken));

        return new CashRegisterReport
        {
            SessionId = session.Id, StoreName = session.Store.Name, RegisterName = session.RegisterName,
            BusinessDate = session.BusinessDate, Status = session.Status, OpenedBy = session.OpenedByUser.FullName,
            OpenedAtUtc = session.OpenedAtUtc, ClosedBy = session.ClosedByUser?.FullName, ClosedAtUtc = session.ClosedAtUtc,
            OpeningCash = session.OpeningCash, TotalSales = totalSales, BillCount = billCount,
            CashSales = drawer.CashSales, UpiSales = upiSales, CardSales = cardSales, CustomerCreditSales = creditSales,
            TotalReturns = totalReturns, CashRefunds = drawer.CashRefunds, CashCreditRepayments = drawer.CashCreditRepayments,
            CashIn = drawer.CashIn, CashOut = drawer.CashOut, SupplierCashPayments = drawer.SupplierCashPayments,
            CashExpenses = drawer.CashExpenses,
            ExpectedCash = drawer.ExpectedCash,
            ActualCash = countedCash is null ? null : Money(countedCash.Value),
            Variance = countedCash is null ? null : Money(countedCash.Value - drawer.ExpectedCash),
            Movements = [.. movementRows.OrderBy(m => m.OccurredAtUtc)],
        };
    }

    /// <summary>
    /// The physical-cash figures alone (no sales/GST breakdown), shared by report generation and
    /// by <see cref="RecordMovementAsync"/>'s overdraft guard so the two can never compute the
    /// drawer balance differently.
    /// </summary>
    private async Task<CashDrawerFigures> CalculateCashDrawerFiguresAsync(
        CashRegisterSession session, DateTime asOfUtc, CancellationToken cancellationToken)
    {
        var payments = db.Sales.AsNoTracking()
            .Where(x => x.Status == SaleStatus.Completed && x.SaleDateUtc >= session.OpenedAtUtc && x.SaleDateUtc <= asOfUtc)
            .SelectMany(x => x.Payments);
        var returns = db.SalesReturns.AsNoTracking()
            .Where(x => x.ReturnDateUtc >= session.OpenedAtUtc && x.ReturnDateUtc <= asOfUtc);
        var movements = db.CashMovements.AsNoTracking()
            .Where(x => x.RegisterSessionId == session.Id && x.OccurredAtUtc <= asOfUtc);

        var expenses = CashExpenseQuery(session, asOfUtc);

        var cashSales = await SumPaymentsAsync(payments, PaymentMethod.Cash, cancellationToken);
        var cashRefunds = Money(await returns.Where(x => x.RefundMethod == RefundMethod.Cash)
            .SumAsync(x => (decimal?)x.RefundAmount, cancellationToken) ?? 0m);
        var cashCreditRepayments = Money(await db.CreditPayments.AsNoTracking()
            .Where(x => x.Method == PaymentMethod.Cash && x.PaymentDateUtc >= session.OpenedAtUtc && x.PaymentDateUtc <= asOfUtc)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m);
        var cashIn = Money(await movements.Where(x => x.Type == CashMovementType.CashIn)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m);
        var cashOut = Money(await movements.Where(x => x.Type == CashMovementType.CashOut)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m);
        var supplierCashPayments = Money(await SupplierCashPaymentQuery(session, asOfUtc)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m);
        var cashExpenses = Money(await expenses
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m);
        var expected = Money(session.OpeningCash + cashSales + cashCreditRepayments + cashIn
            - cashRefunds - cashOut - supplierCashPayments - cashExpenses);

        return new CashDrawerFigures(
            cashSales, cashRefunds, cashCreditRepayments, cashIn, cashOut, supplierCashPayments,
            cashExpenses, expected);
    }

    /// <summary>
    /// Cash spent on store expenses inside the session window. Derived from the Expenses table
    /// rather than mirrored into a CashMovement row — the same treatment cash sales, cash refunds,
    /// udhaar repayments and supplier cash payments already get, so the expense itself stays the
    /// single financial record with nothing to double-count or leave orphaned.
    ///
    /// <para><b>Membership uses <c>CreatedAtUtc</c>, not <c>ExpenseDateUtc</c>.</b> Unlike a supplier
    /// payment — whose date is always the moment it was recorded — an expense carries a
    /// user-editable ACCOUNTING date that may legitimately be backdated to last week. The cash,
    /// however, left the drawer when the record was made. Keying on the accounting date would let an
    /// operator move money in and out of a register session just by editing a date field.</para>
    ///
    /// <para>Only <see cref="PaymentMethod.Cash"/> moves physical money; UPI/Card/CustomerCredit
    /// expenses are real expenses but never touch the drawer.</para>
    /// </summary>
    private IQueryable<Expense> CashExpenseQuery(CashRegisterSession session, DateTime asOfUtc) =>
        db.Expenses.AsNoTracking().Where(x =>
            x.PaymentMethod == PaymentMethod.Cash
            && x.CreatedAtUtc >= session.OpenedAtUtc
            && x.CreatedAtUtc <= asOfUtc);

    private async Task<List<CashMovementRow>> CashExpenseRowsAsync(
        CashRegisterSession session, DateTime asOfUtc, CancellationToken cancellationToken) =>
        await CashExpenseQuery(session, asOfUtc)
            .OrderBy(x => x.CreatedAtUtc)
            .Select(x => new CashMovementRow(
                x.Id,
                CashRegisterMovementKind.Expense,
                x.Amount,
                x.CategoryNameSnapshot + " (" + x.ExpenseNumber + ")",
                x.CreatedByUser != null ? x.CreatedByUser.FullName : "System",
                x.CreatedAtUtc))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// Cash paid to suppliers inside the session window. Derived from the SupplierPayments table
    /// rather than mirrored into a CashMovement row: that is the same treatment cash sales, cash
    /// refunds and udhaar repayments already get, and it means the supplier payment itself is the
    /// single financial record — nothing to double-count, duplicate on retry, or leave orphaned if
    /// a write half-fails. Only <see cref="PaymentMethod.Cash"/> moves physical money.
    /// </summary>
    private IQueryable<SupplierPayment> SupplierCashPaymentQuery(CashRegisterSession session, DateTime asOfUtc) =>
        db.SupplierPayments.AsNoTracking().Where(x =>
            x.Method == PaymentMethod.Cash
            && x.PaymentDateUtc >= session.OpenedAtUtc
            && x.PaymentDateUtc <= asOfUtc);

    private async Task<List<CashMovementRow>> SupplierCashPaymentRowsAsync(
        CashRegisterSession session, DateTime asOfUtc, CancellationToken cancellationToken) =>
        await SupplierCashPaymentQuery(session, asOfUtc)
            .Include(x => x.Supplier).Include(x => x.RecordedByUser)
            .OrderBy(x => x.PaymentDateUtc)
            .Select(x => new CashMovementRow(
                x.Id,
                CashRegisterMovementKind.SupplierPayment,
                x.Amount,
                x.Supplier.Name,
                x.RecordedByUser != null ? x.RecordedByUser.FullName : "System",
                x.PaymentDateUtc))
            .ToListAsync(cancellationToken);

    private readonly record struct CashDrawerFigures(
        decimal CashSales, decimal CashRefunds, decimal CashCreditRepayments, decimal CashIn, decimal CashOut,
        decimal SupplierCashPayments, decimal CashExpenses, decimal ExpectedCash);

    private async Task<CashRegisterReport> BuildClosedReportAsync(int sessionId, CancellationToken cancellationToken)
    {
        var x = await db.CashRegisterSessions.AsNoTracking()
            .Include(s => s.Store).Include(s => s.OpenedByUser).Include(s => s.ClosedByUser)
            .Include(s => s.Movements).ThenInclude(m => m.PerformedByUser)
            .SingleOrDefaultAsync(s => s.Id == sessionId, cancellationToken)
            ?? throw new InvalidOperationException("Register session not found.");
        if (x.Status != CashRegisterStatus.Closed) throw new InvalidOperationException("A Z Report is available only after the register is closed.");

        // Figures come from the frozen snapshot, but the movement *lines* are still listed from
        // their source tables. That is safe for a closed session: its window is fixed, so
        // CashMovements, supplier payments and cash expenses inside it can no longer change —
        // CashMovements and SupplierPayments are append-only, and ExpenseService refuses to edit or
        // delete a cash-affecting expense that falls inside a closed session precisely so this
        // listing can never drift from the frozen figure above it.
        var movementRows = x.Movements
            .Select(m => new CashMovementRow(
                m.Id,
                m.Type == CashMovementType.CashIn ? CashRegisterMovementKind.CashIn : CashRegisterMovementKind.CashOut,
                m.Amount, m.Reason, m.PerformedByUser.FullName, m.OccurredAtUtc))
            .ToList();
        movementRows.AddRange(await SupplierCashPaymentRowsAsync(x, x.ClosedAtUtc ?? DateTime.UtcNow, cancellationToken));
        movementRows.AddRange(await CashExpenseRowsAsync(x, x.ClosedAtUtc ?? DateTime.UtcNow, cancellationToken));

        return new CashRegisterReport
        {
            SessionId = x.Id, StoreName = x.Store.Name, RegisterName = x.RegisterName, BusinessDate = x.BusinessDate,
            Status = x.Status, OpenedBy = x.OpenedByUser.FullName, OpenedAtUtc = x.OpenedAtUtc,
            ClosedBy = x.ClosedByUser?.FullName, ClosedAtUtc = x.ClosedAtUtc, OpeningCash = x.OpeningCash,
            TotalSales = x.TotalSales, BillCount = x.BillCount, CashSales = x.CashSales, UpiSales = x.UpiSales,
            CardSales = x.CardSales, CustomerCreditSales = x.CustomerCreditSales, TotalReturns = x.TotalReturns,
            CashRefunds = x.CashRefunds, CashCreditRepayments = x.CashCreditRepayments, CashIn = x.CashIn,
            CashOut = x.CashOut, SupplierCashPayments = x.SupplierCashPayments,
            CashExpenses = x.CashExpenses,
            ExpectedCash = x.ExpectedCash, ActualCash = x.ActualCash, Variance = x.Variance,
            Movements = [.. movementRows.OrderBy(m => m.OccurredAtUtc)],
        };
    }

    private static async Task<decimal> SumPaymentsAsync(IQueryable<Payment> payments, PaymentMethod method, CancellationToken token) =>
        Money(await payments.Where(x => x.Method == method).SumAsync(x => (decimal?)x.Amount, token) ?? 0m);

    private static decimal Money(decimal value) => Math.Round(value, 2, MidpointRounding.AwayFromZero);
}
