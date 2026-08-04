namespace Kirana.Application.Reports;

public sealed class ExpenseDailyRow
{
    public DateOnly Date { get; init; }
    public decimal Amount { get; init; }
    public int Count { get; init; }
}

public sealed class ExpenseMonthlyRow
{
    public int Year { get; init; }
    public int Month { get; init; }
    public string Label { get; init; } = string.Empty;
    public decimal Amount { get; init; }
    public int Count { get; init; }
}
