using Kirana.Application.Abstractions;
using Kirana.Application.Authentication;
using Kirana.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Kirana.Application.Expenses;

public sealed class ExpenseService(
    IKiranaDbContext db, ISequenceGenerator sequenceGenerator, IAuditLogger auditLogger, IPermissionEnforcer permissionEnforcer)
    : IExpenseService
{
    private const string ExpenseSequenceKey = "Expense";
    private const string ExpensePrefix = "EXP";
    private const int ExpensePadding = 6;

    public async Task<Expense> CreateAsync(CreateExpenseRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Expense amount must be greater than zero.", nameof(request));
        }

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId, cancellationToken)
            ?? throw new InvalidOperationException("Expense category not found.");

        if (!category.IsActive)
        {
            throw new InvalidOperationException($"'{category.Name}' is inactive and cannot take new expenses.");
        }

        var expenseNumber = await sequenceGenerator.NextAsync(ExpenseSequenceKey, ExpensePrefix, ExpensePadding, cancellationToken);

        var expense = new Expense
        {
            ExpenseNumber = expenseNumber,
            ExpenseDateUtc = request.ExpenseDateUtc ?? DateTime.UtcNow,
            ExpenseCategory = category,
            // Snapshotted so renaming the category later does not rewrite what past receipts say.
            CategoryNameSnapshot = category.Name,
            Amount = request.Amount,
            PaymentMethod = request.PaymentMethod,
            Description = Normalize(request.Description),
            ReferenceNumber = Normalize(request.ReferenceNumber),
            Notes = Normalize(request.Notes),
            CreatedByUserId = request.PerformedByUserId,
        };

        db.Expenses.Add(expense);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.PerformedByUserId, "ExpenseCreated", nameof(Expense), expense.Id.ToString(),
            newValue: $"{expenseNumber} — ₹{expense.Amount:0.00} ({category.Name})",
            cancellationToken: cancellationToken);

        return expense;
    }

    public async Task<Expense> UpdateAsync(int expenseId, UpdateExpenseRequest request, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(request.PerformedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        if (request.Amount <= 0)
        {
            throw new ArgumentException("Expense amount must be greater than zero.", nameof(request));
        }

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId, cancellationToken)
            ?? throw new InvalidOperationException("Expense not found.");

        var category = await db.ExpenseCategories.FirstOrDefaultAsync(c => c.Id == request.ExpenseCategoryId, cancellationToken)
            ?? throw new InvalidOperationException("Expense category not found.");

        // Blocked when EITHER the stored or the requested method is Cash: editing a cash expense
        // would change a reconciled drawer, and flipping a non-cash expense TO cash would inject
        // money into a session whose Z report is already frozen.
        await EnsureNotInClosedRegisterAsync(expense, request.PaymentMethod, cancellationToken);

        var previous = $"₹{expense.Amount:0.00} ({expense.CategoryNameSnapshot})";

        expense.ExpenseCategoryId = category.Id;
        expense.CategoryNameSnapshot = category.Name;
        expense.Amount = request.Amount;
        expense.ExpenseDateUtc = request.ExpenseDateUtc;
        expense.PaymentMethod = request.PaymentMethod;
        expense.Description = Normalize(request.Description);
        expense.ReferenceNumber = Normalize(request.ReferenceNumber);
        expense.Notes = Normalize(request.Notes);
        expense.UpdatedAtUtc = DateTime.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            request.PerformedByUserId, "ExpenseUpdated", nameof(Expense), expense.Id.ToString(),
            previousValue: previous, newValue: $"₹{expense.Amount:0.00} ({category.Name})",
            cancellationToken: cancellationToken);

        return expense;
    }

    public async Task DeleteAsync(int expenseId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        var expense = await db.Expenses.FirstOrDefaultAsync(e => e.Id == expenseId, cancellationToken)
            ?? throw new InvalidOperationException("Expense not found.");

        await EnsureNotInClosedRegisterAsync(expense, expense.PaymentMethod, cancellationToken);

        var description = $"{expense.ExpenseNumber} — ₹{expense.Amount:0.00} ({expense.CategoryNameSnapshot})";

        db.Expenses.Remove(expense);
        await db.SaveChangesAsync(cancellationToken);

        await auditLogger.RecordAsync(
            performedByUserId, "ExpenseDeleted", nameof(Expense), expenseId.ToString(),
            previousValue: description, cancellationToken: cancellationToken);
    }

    public async Task<Expense?> GetByIdAsync(int expenseId, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        return await db.Expenses.AsNoTracking()
            .Include(e => e.ExpenseCategory)
            .Include(e => e.CreatedByUser)
            .FirstOrDefaultAsync(e => e.Id == expenseId, cancellationToken);
    }

    public async Task<IReadOnlyList<Expense>> SearchAsync(
        ExpenseSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        return await ApplyFilters(db.Expenses.AsNoTracking().Include(e => e.ExpenseCategory), query)
            .OrderByDescending(e => e.ExpenseDateUtc)
            .Take(query.MaxResults)
            .ToListAsync(cancellationToken);
    }

    public async Task<ExpenseTotals> GetTotalsAsync(
        ExpenseSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default)
    {
        await permissionEnforcer.EnsureHasPermissionAsync(performedByUserId, PermissionKeys.ExpensesManage, cancellationToken);

        // Totals come from the whole filtered set, not the truncated page the list shows, so the
        // figure on screen is the real total for the filter.
        var filtered = ApplyFilters(db.Expenses.AsNoTracking(), query);

        var byCategory = await filtered
            .GroupBy(e => new { e.ExpenseCategoryId, e.CategoryNameSnapshot })
            .Select(g => new ExpenseCategoryTotal
            {
                ExpenseCategoryId = g.Key.ExpenseCategoryId,
                CategoryName = g.Key.CategoryNameSnapshot,
                TotalAmount = g.Sum(e => e.Amount),
                Count = g.Count(),
            })
            .ToListAsync(cancellationToken);

        return new ExpenseTotals
        {
            TotalAmount = byCategory.Sum(c => c.TotalAmount),
            Count = byCategory.Sum(c => c.Count),
            ByCategory = byCategory.OrderByDescending(c => c.TotalAmount).ToList(),
        };
    }

    /// <summary>
    /// Refuses to change an expense whose cash already sits inside a CLOSED register session
    /// (Phase 16A-1).
    ///
    /// <para>A closed session's Z report is an immutable record of what a human physically counted.
    /// Cash expenses feed <c>ExpectedCash</c>, so editing the amount, flipping the payment method,
    /// or deleting the row would silently disagree with a variance that has already been signed
    /// off. Rather than let history drift, the edit is refused.</para>
    ///
    /// <para>Membership uses <see cref="Entity.CreatedAtUtc"/> for the same reason the register
    /// calculation does — <c>ExpenseDateUtc</c> is a user-editable accounting date, so keying on it
    /// would let someone edit their way out of this check.</para>
    ///
    /// <para>Non-cash expenses in a closed window stay freely editable: they never touched the
    /// drawer, so nothing about the session's reconciliation depends on them. Only a change that
    /// involves cash on either side is blocked.</para>
    /// </summary>
    private async Task EnsureNotInClosedRegisterAsync(
        Expense expense, PaymentMethod requestedMethod, CancellationToken cancellationToken)
    {
        if (expense.PaymentMethod != PaymentMethod.Cash && requestedMethod != PaymentMethod.Cash)
        {
            return;
        }

        var closedSession = await db.CashRegisterSessions.AsNoTracking()
            .Where(s => s.Status == CashRegisterStatus.Closed
                && s.ClosedAtUtc != null
                && expense.CreatedAtUtc >= s.OpenedAtUtc
                && expense.CreatedAtUtc <= s.ClosedAtUtc)
            .OrderByDescending(s => s.OpenedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (closedSession is not null)
        {
            throw new InvalidOperationException(
                $"This cash expense belongs to a closed register ({closedSession.RegisterName}, "
                + $"{closedSession.BusinessDate:dd MMM yyyy}) and cannot be modified.");
        }
    }

    private static IQueryable<Expense> ApplyFilters(IQueryable<Expense> expenses, ExpenseSearchQuery query)
    {
        var text = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var like = $"%{text}%";
            expenses = expenses.Where(e =>
                EF.Functions.Like(e.ExpenseNumber, like)
                || EF.Functions.Like(e.CategoryNameSnapshot, like)
                || (e.Description != null && EF.Functions.Like(e.Description, like))
                || (e.ReferenceNumber != null && EF.Functions.Like(e.ReferenceNumber, like)));
        }

        if (query.ExpenseCategoryId is { } categoryId)
        {
            expenses = expenses.Where(e => e.ExpenseCategoryId == categoryId);
        }

        if (query.FromDateUtc is { } from)
        {
            expenses = expenses.Where(e => e.ExpenseDateUtc >= from);
        }

        if (query.ToDateUtc is { } to)
        {
            expenses = expenses.Where(e => e.ExpenseDateUtc <= to);
        }

        if (query.PaymentMethod is { } method)
        {
            expenses = expenses.Where(e => e.PaymentMethod == method);
        }

        return expenses;
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
