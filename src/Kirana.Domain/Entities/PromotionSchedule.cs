using Kirana.Domain.Common;

namespace Kirana.Domain.Entities;

/// <summary>UTC boundaries make the schedule stable across daylight-saving and machine timezone
/// changes. Optional daily windows are stored as clock times in the named source timezone.</summary>
public class PromotionSchedule : Entity
{
    public int PromotionId { get; set; }
    public Promotion Promotion { get; set; } = null!;
    public DateTime StartAtUtc { get; set; }
    public DateTime EndAtUtc { get; set; }
    public TimeSpan? DailyStartTime { get; set; }
    public TimeSpan? DailyEndTime { get; set; }
    public string TimeZoneId { get; set; } = TimeZoneInfo.Local.Id;
}
