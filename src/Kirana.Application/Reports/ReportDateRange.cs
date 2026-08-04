namespace Kirana.Application.Reports;

/// <summary>The date-filter presets the Dashboard and every report screen share (PRD §35, §51).</summary>
public enum ReportDatePreset
{
    Today,
    Yesterday,
    ThisWeek,
    ThisMonth,
    ThisYear,
    Custom,
}

/// <summary>
/// A resolved, half-open UTC instant range ([StartUtc, EndUtc)) plus the label it should be shown
/// under. Kept separate from the individual report services so the "what does 'this week' mean"
/// question is answered once and tested once.
///
/// Boundaries are computed from local wall-clock days, not UTC days: every timestamp in the
/// database (<c>Sale.SaleDateUtc</c> etc.) is stored in UTC, but a shopkeeper's "today" is their
/// local calendar day. Resolving "Today" as <c>DateTime.UtcNow.Date</c> would roll over at
/// midnight UTC — 5:30am in India — which is wrong for a dashboard whose entire point is showing
/// "what happened today."
/// </summary>
public sealed class ReportDateRange
{
    public required DateTime StartUtc { get; init; }

    /// <summary>Exclusive upper bound — a row belongs in the range when
    /// <c>StartUtc &lt;= timestamp &amp;&amp; timestamp &lt; EndUtc</c>.</summary>
    public required DateTime EndUtc { get; init; }

    public required string Label { get; init; }

    public required ReportDatePreset Preset { get; init; }

    /// <param name="nowLocalOverride">Injected in tests so "Today"/"This Week" etc. resolve
    /// deterministically instead of depending on the clock the test happens to run at.</param>
    public static ReportDateRange Resolve(
        ReportDatePreset preset,
        DateOnly? customFromLocal = null,
        DateOnly? customToLocal = null,
        DateTime? nowLocalOverride = null)
    {
        var todayLocal = (nowLocalOverride ?? DateTime.Now).Date;
        DateTime startLocal;
        DateTime endLocalExclusive;
        string label;

        switch (preset)
        {
            case ReportDatePreset.Today:
                startLocal = todayLocal;
                endLocalExclusive = todayLocal.AddDays(1);
                label = "Today";
                break;

            case ReportDatePreset.Yesterday:
                startLocal = todayLocal.AddDays(-1);
                endLocalExclusive = todayLocal;
                label = "Yesterday";
                break;

            case ReportDatePreset.ThisWeek:
                // Week starts Monday, matching the Indian retail convention and ISO 8601.
                var daysSinceMonday = ((int)todayLocal.DayOfWeek + 6) % 7;
                startLocal = todayLocal.AddDays(-daysSinceMonday);
                endLocalExclusive = startLocal.AddDays(7);
                label = "This Week";
                break;

            case ReportDatePreset.ThisMonth:
                startLocal = new DateTime(todayLocal.Year, todayLocal.Month, 1);
                endLocalExclusive = startLocal.AddMonths(1);
                label = "This Month";
                break;

            case ReportDatePreset.ThisYear:
                startLocal = new DateTime(todayLocal.Year, 1, 1);
                endLocalExclusive = startLocal.AddYears(1);
                label = "This Year";
                break;

            case ReportDatePreset.Custom:
                if (customFromLocal is null || customToLocal is null)
                {
                    throw new ArgumentException("A custom range needs both a start and an end date.");
                }

                startLocal = customFromLocal.Value.ToDateTime(TimeOnly.MinValue);
                endLocalExclusive = customToLocal.Value.ToDateTime(TimeOnly.MinValue).AddDays(1);

                if (endLocalExclusive <= startLocal)
                {
                    throw new ArgumentException("The end date must be on or after the start date.");
                }

                label = customFromLocal == customToLocal
                    ? customFromLocal.Value.ToString("dd-MMM-yyyy")
                    : $"{customFromLocal:dd-MMM-yyyy} to {customToLocal:dd-MMM-yyyy}";
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(preset));
        }

        return new ReportDateRange
        {
            StartUtc = DateTime.SpecifyKind(startLocal, DateTimeKind.Local).ToUniversalTime(),
            EndUtc = DateTime.SpecifyKind(endLocalExclusive, DateTimeKind.Local).ToUniversalTime(),
            Label = label,
            Preset = preset,
        };
    }
}
