using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

/// <summary>
/// One rendered bar: <see cref="BarSize"/> is pre-scaled to pixels in the view-model layer (not a
/// XAML converter) so the chart's XAML only has to bind numbers, keeping the DataTemplate itself
/// simple and free of any C# resource lookups that would need to know the current theme — the bar
/// colour still comes from a plain <c>{ThemeResource}</c> in the template, which WinUI re-resolves
/// automatically on every theme change with no code involved.
/// </summary>
public sealed class ChartBarItemViewModel
{
    public string Label { get; init; } = string.Empty;
    public string ValueText { get; init; } = string.Empty;
    public double BarSize { get; init; }
    public bool IsEmpty { get; init; }
}

public static class ChartViewModelFactory
{
    /// <summary>A visible sliver for zero-value bars so the label/axis position is never blank —
    /// distinguishing "no data drawn" from "genuinely sold nothing that day."</summary>
    private const double MinimumBarSize = 3;

    public static IReadOnlyList<ChartBarItemViewModel> BuildBars(ChartSeries series, double maxSize)
    {
        if (series.Points.Count == 0)
        {
            return [];
        }

        var maxValue = series.Points.Max(p => p.Value);

        return series.Points.Select(p => new ChartBarItemViewModel
        {
            Label = p.Label,
            ValueText = FormatCurrency(p.Value),
            BarSize = maxValue <= 0 ? MinimumBarSize : Math.Max(MinimumBarSize, (double)(p.Value / maxValue) * maxSize),
            IsEmpty = p.Value <= 0,
        }).ToList();
    }

    /// <summary>Two series sharing one label axis (Sales vs Expenses), scaled against the larger
    /// series so both bars are comparable on one chart.</summary>
    public static (IReadOnlyList<ChartBarItemViewModel> First, IReadOnlyList<ChartBarItemViewModel> Second) BuildPairedBars(
        ChartSeries first, ChartSeries second, double maxSize)
    {
        var sharedMax = new[] { first.Points.Count == 0 ? 0 : first.Points.Max(p => p.Value), second.Points.Count == 0 ? 0 : second.Points.Max(p => p.Value) }.Max();

        IReadOnlyList<ChartBarItemViewModel> Scale(ChartSeries series) =>
            series.Points.Select(p => new ChartBarItemViewModel
            {
                Label = p.Label,
                ValueText = FormatCurrency(p.Value),
                BarSize = sharedMax <= 0 ? MinimumBarSize : Math.Max(MinimumBarSize, (double)(p.Value / sharedMax) * maxSize),
                IsEmpty = p.Value <= 0,
            }).ToList();

        return (Scale(first), Scale(second));
    }

    private static string FormatCurrency(decimal amount) =>
        "₹" + amount.ToString("N2", System.Globalization.CultureInfo.GetCultureInfo("en-IN"));
}
