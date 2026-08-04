using Kirana.Application.Reports;

namespace Kirana.Tests.Reports;

/// <summary>
/// Date-range resolution (PRD §51 "date filters"). All tests fix "now" to a known Wednesday
/// (2026-08-05) so "This Week"/"Today" etc. are deterministic rather than depending on when the
/// test happens to run.
/// </summary>
public class ReportDateRangeTests
{
    private static readonly DateTime FixedNow = new(2026, 8, 5, 15, 30, 0); // Wednesday

    [Fact]
    public void Today_IsMidnightToMidnightLocal()
    {
        var range = ReportDateRange.Resolve(ReportDatePreset.Today, nowLocalOverride: FixedNow);

        var expectedStart = DateTime.SpecifyKind(new DateTime(2026, 8, 5), DateTimeKind.Local).ToUniversalTime();
        var expectedEnd = DateTime.SpecifyKind(new DateTime(2026, 8, 6), DateTimeKind.Local).ToUniversalTime();

        Assert.Equal(expectedStart, range.StartUtc);
        Assert.Equal(expectedEnd, range.EndUtc);
        Assert.Equal("Today", range.Label);
    }

    [Fact]
    public void Yesterday_IsTheDayBeforeToday()
    {
        var range = ReportDateRange.Resolve(ReportDatePreset.Yesterday, nowLocalOverride: FixedNow);

        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 8, 4), DateTimeKind.Local).ToUniversalTime(), range.StartUtc);
        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 8, 5), DateTimeKind.Local).ToUniversalTime(), range.EndUtc);
    }

    [Fact]
    public void ThisWeek_StartsOnMonday()
    {
        // FixedNow is Wednesday 5-Aug-2026, so the week started Monday 3-Aug-2026.
        var range = ReportDateRange.Resolve(ReportDatePreset.ThisWeek, nowLocalOverride: FixedNow);

        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 8, 3), DateTimeKind.Local).ToUniversalTime(), range.StartUtc);
        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 8, 10), DateTimeKind.Local).ToUniversalTime(), range.EndUtc);
    }

    [Fact]
    public void ThisWeek_WhenTodayIsMonday_StartsToday()
    {
        var monday = new DateTime(2026, 8, 3, 9, 0, 0);
        var range = ReportDateRange.Resolve(ReportDatePreset.ThisWeek, nowLocalOverride: monday);

        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 8, 3), DateTimeKind.Local).ToUniversalTime(), range.StartUtc);
    }

    [Fact]
    public void ThisMonth_StartsOnTheFirst()
    {
        var range = ReportDateRange.Resolve(ReportDatePreset.ThisMonth, nowLocalOverride: FixedNow);

        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 8, 1), DateTimeKind.Local).ToUniversalTime(), range.StartUtc);
        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 9, 1), DateTimeKind.Local).ToUniversalTime(), range.EndUtc);
    }

    [Fact]
    public void ThisYear_StartsOnJanuaryFirst()
    {
        var range = ReportDateRange.Resolve(ReportDatePreset.ThisYear, nowLocalOverride: FixedNow);

        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 1, 1), DateTimeKind.Local).ToUniversalTime(), range.StartUtc);
        Assert.Equal(DateTime.SpecifyKind(new DateTime(2027, 1, 1), DateTimeKind.Local).ToUniversalTime(), range.EndUtc);
    }

    [Fact]
    public void Custom_IsInclusiveOfBothEndpoints()
    {
        var from = new DateOnly(2026, 7, 1);
        var to = new DateOnly(2026, 7, 5);
        var range = ReportDateRange.Resolve(ReportDatePreset.Custom, from, to);

        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 7, 1), DateTimeKind.Local).ToUniversalTime(), range.StartUtc);
        // Exclusive end is the day AFTER 5-Jul, so 5-Jul itself is fully included.
        Assert.Equal(DateTime.SpecifyKind(new DateTime(2026, 7, 6), DateTimeKind.Local).ToUniversalTime(), range.EndUtc);
    }

    [Fact]
    public void Custom_SingleDay_ProducesAOneDayRange()
    {
        var day = new DateOnly(2026, 7, 1);
        var range = ReportDateRange.Resolve(ReportDatePreset.Custom, day, day);

        Assert.Equal(TimeSpan.FromDays(1), range.EndUtc - range.StartUtc);
    }

    [Fact]
    public void Custom_Throws_WhenDatesAreMissing() =>
        Assert.Throws<ArgumentException>(() => ReportDateRange.Resolve(ReportDatePreset.Custom));

    [Fact]
    public void Custom_Throws_WhenEndIsBeforeStart() =>
        Assert.Throws<ArgumentException>(() => ReportDateRange.Resolve(
            ReportDatePreset.Custom, new DateOnly(2026, 7, 10), new DateOnly(2026, 7, 1)));
}
