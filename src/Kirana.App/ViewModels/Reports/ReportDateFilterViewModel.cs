using CommunityToolkit.Mvvm.ComponentModel;
using Kirana.Application.Reports;

namespace Kirana.App.ViewModels.Reports;

/// <summary>
/// The date-range picker every report tab shares (PRD §51 "date filters"): five presets plus a
/// custom from/to pair. Composed into each tab's own view model rather than inherited, since every
/// tab already derives from <see cref="ObservableObject"/> for its own state and C# has no multiple
/// inheritance — a tab's XAML binds through <c>ViewModel.DateFilter.SelectedPresetName</c> etc.
/// </summary>
public sealed partial class ReportDateFilterViewModel : ObservableObject
{
    public IReadOnlyList<string> PresetNames { get; } = ["Today", "Yesterday", "This Week", "This Month", "This Year", "Custom Range"];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsCustomSelected))]
    private string _selectedPresetName = "Today";

    [ObservableProperty]
    private DateTimeOffset? _customFrom = DateTimeOffset.Now.AddDays(-6);

    [ObservableProperty]
    private DateTimeOffset? _customTo = DateTimeOffset.Now;

    public bool IsCustomSelected => SelectedPresetName == "Custom Range";

    /// <summary>Resolves the current selection. Throws the same <see cref="ArgumentException"/>
    /// <see cref="ReportDateRange.Resolve"/> would if Custom is selected with a missing/invalid
    /// range — callers show that message rather than pre-validating it twice.</summary>
    public ReportDateRange Resolve()
    {
        var preset = SelectedPresetName switch
        {
            "Yesterday" => ReportDatePreset.Yesterday,
            "This Week" => ReportDatePreset.ThisWeek,
            "This Month" => ReportDatePreset.ThisMonth,
            "This Year" => ReportDatePreset.ThisYear,
            "Custom Range" => ReportDatePreset.Custom,
            _ => ReportDatePreset.Today,
        };

        return ReportDateRange.Resolve(
            preset,
            CustomFrom.HasValue ? DateOnly.FromDateTime(CustomFrom.Value.Date) : null,
            CustomTo.HasValue ? DateOnly.FromDateTime(CustomTo.Value.Date) : null);
    }
}
