namespace Kirana.Application.Reports;

/// <summary>One labelled value on a chart axis — a day, a week, a category, a customer name.</summary>
public sealed class ChartPoint
{
    public string Label { get; init; } = string.Empty;
    public decimal Value { get; init; }
}

/// <summary>One series of points, e.g. "Sales" or "Expenses" on the Sales-vs-Expenses chart. Most
/// charts have exactly one series; a couple (Sales vs Expenses) have two sharing the same labels.</summary>
public sealed class ChartSeries
{
    public string Name { get; init; } = string.Empty;
    public IReadOnlyList<ChartPoint> Points { get; init; } = [];
}
