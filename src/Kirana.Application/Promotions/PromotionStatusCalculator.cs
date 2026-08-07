using Kirana.Domain.Entities;

namespace Kirana.Application.Promotions;

public static class PromotionStatusCalculator
{
    public static PromotionStatus Calculate(Promotion promotion, DateTime atUtc)
    {
        if (!promotion.IsActive)
        {
            return promotion.Status == PromotionStatus.Draft ? PromotionStatus.Draft : PromotionStatus.Disabled;
        }

        if (promotion.Schedule is null || atUtc < promotion.Schedule.StartAtUtc)
        {
            return PromotionStatus.Scheduled;
        }

        if (atUtc >= promotion.Schedule.EndAtUtc)
        {
            return PromotionStatus.Expired;
        }

        if (promotion.MaximumUsage is { } maximum && promotion.CurrentUsage >= maximum)
        {
            return PromotionStatus.Expired;
        }

        if (!IsInsideDailyWindow(promotion.Schedule, atUtc))
        {
            return PromotionStatus.Scheduled;
        }

        return PromotionStatus.Running;
    }

    private static bool IsInsideDailyWindow(PromotionSchedule schedule, DateTime atUtc)
    {
        if (schedule.DailyStartTime is null || schedule.DailyEndTime is null)
        {
            return true;
        }

        TimeZoneInfo zone;
        try { zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId); }
        catch { zone = TimeZoneInfo.Utc; }
        var localTime = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(atUtc, DateTimeKind.Utc), zone).TimeOfDay;
        return schedule.DailyStartTime <= schedule.DailyEndTime
            ? localTime >= schedule.DailyStartTime && localTime <= schedule.DailyEndTime
            : localTime >= schedule.DailyStartTime || localTime <= schedule.DailyEndTime;
    }
}
