using Kirana.Domain.Entities;

namespace Kirana.Application.Expenses;

/// <summary>
/// Expense categories (PRD §32). Gated by <see cref="PermissionKeys.ExpensesManage"/> — what a shop
/// spends is financial data a cashier has no business seeing.
/// </summary>
public interface IExpenseCategoryService
{
    Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(
        bool includeInactive, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<ExpenseCategory?> GetByIdAsync(int categoryId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<ExpenseCategory> CreateAsync(CreateExpenseCategoryRequest request, CancellationToken cancellationToken = default);

    Task<ExpenseCategory> UpdateAsync(int categoryId, UpdateExpenseCategoryRequest request, CancellationToken cancellationToken = default);

    Task<ExpenseCategory> SetActiveAsync(
        int categoryId, bool isActive, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Deletes a category. Refused when expenses are booked against it, or when it is one
    /// of the seeded defaults — deactivate those instead.</summary>
    Task DeleteAsync(int categoryId, int? performedByUserId, CancellationToken cancellationToken = default);

    /// <summary>Idempotently inserts the default headings on an existing database that predates
    /// this phase, mirroring how permissions are back-filled on upgrade.</summary>
    Task SeedDefaultsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Expenses (PRD §32). Deliberately never touches inventory or stock movements.
/// </summary>
public interface IExpenseService
{
    Task<Expense> CreateAsync(CreateExpenseRequest request, CancellationToken cancellationToken = default);

    Task<Expense> UpdateAsync(int expenseId, UpdateExpenseRequest request, CancellationToken cancellationToken = default);

    Task DeleteAsync(int expenseId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<Expense?> GetByIdAsync(int expenseId, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Expense>> SearchAsync(
        ExpenseSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);

    Task<ExpenseTotals> GetTotalsAsync(
        ExpenseSearchQuery query, int? performedByUserId, CancellationToken cancellationToken = default);
}
