namespace WithWindows.Core.Scheduling;

public static class ScheduleCalculator
{
    public static DateTimeOffset? NextAfter(
        TaskSchedule schedule,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(timeZone);

        return schedule.Kind switch
        {
            ScheduleKind.Once => NextOnce(schedule, now),
            _ => null,
        };
    }

    public static DateTimeOffset? LatestAtOrBefore(
        TaskSchedule schedule,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(timeZone);

        return schedule.Kind switch
        {
            ScheduleKind.Once => LatestOnce(schedule, now),
            _ => null,
        };
    }

    private static DateTimeOffset? NextOnce(TaskSchedule schedule, DateTimeOffset now)
        => schedule.OneTimeConsumedAt is null
            && schedule.OneTimeRunAt is { } runAt
            && runAt > now
                ? runAt
                : null;

    private static DateTimeOffset? LatestOnce(TaskSchedule schedule, DateTimeOffset now)
        => schedule.OneTimeConsumedAt is null
            && schedule.OneTimeRunAt is { } runAt
            && runAt <= now
                ? runAt
                : null;
}
