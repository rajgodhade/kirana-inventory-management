using Kirana.Domain.Entities;

namespace Kirana.Application.Expenses;

public sealed class CreateExpenseCategoryRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int? PerformedByUserId { get; init; }
}

public sealed class UpdateExpenseCategoryRequest
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public int? PerformedByUserId { get; init; }
}

public sealed class CreateExpenseRequest
{
    public int ExpenseCategoryId { get; init; }
    public decimal Amount { get; init; }
    public DateTime? ExpenseDateUtc { get; init; }
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;
    public string? Description { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? Notes { get; init; }
    public int? PerformedByUserId { get; init; }
}

public sealed class UpdateExpenseRequest
{
    public int ExpenseCategoryId { get; init; }
    public decimal Amount { get; init; }
    public DateTime ExpenseDateUtc { get; init; }
    public PaymentMethod PaymentMethod { get; init; } = PaymentMethod.Cash;
    public string? Description { get; init; }
    public string? ReferenceNumber { get; init; }
    public string? Notes { get; init; }
    public int? PerformedByUserId { get; init; }
}

public sealed class ExpenseSearchQuery
{
    public string? SearchText { get; init; }
    public int? ExpenseCategoryId { get; init; }
    public DateTime? FromDateUtc { get; init; }
    public DateTime? ToDateUtc { get; init; }
    public PaymentMethod? PaymentMethod { get; init; }
    public int MaxResults { get; init; } = 200;
}

/// <summary>Totals for the current filter, so the list screen can show what the filtered set adds
/// up to without the UI re-summing a truncated page.</summary>
public sealed class ExpenseTotals
{
    public decimal TotalAmount { get; init; }
    public int Count { get; init; }
    public IReadOnlyList<ExpenseCategoryTotal> ByCategory { get; init; } = [];
}

public sealed class ExpenseCategoryTotal
{
    public int ExpenseCategoryId { get; init; }
    public string CategoryName { get; init; } = string.Empty;
    public decimal TotalAmount { get; init; }
    public int Count { get; init; }
}
