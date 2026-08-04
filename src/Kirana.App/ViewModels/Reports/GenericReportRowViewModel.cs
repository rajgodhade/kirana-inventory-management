namespace Kirana.App.ViewModels.Reports;

/// <summary>
/// One row in a "pick a report type from a dropdown" tab (Products, Inventory). The underlying
/// report services return different DTO shapes per report type (a sold-products row differs from a
/// dead-stock row), so each tab normalizes whichever DTO it fetched into this one shape rather than
/// switching the DataTemplate itself — the table stays one simple, reusable ItemsControl no matter
/// which report is selected.
/// </summary>
public sealed class GenericReportRowViewModel
{
    public string Primary { get; init; } = string.Empty;
    public string Secondary { get; init; } = string.Empty;
    public string Column1Label { get; init; } = string.Empty;
    public string Column1Value { get; init; } = string.Empty;
    public string Column2Label { get; init; } = string.Empty;
    public string Column2Value { get; init; } = string.Empty;
    public string Column3Label { get; init; } = string.Empty;
    public string Column3Value { get; init; } = string.Empty;
}
