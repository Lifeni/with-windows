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
            ScheduleKind.Daily => NextDaily(schedule, now, timeZone),
            ScheduleKind.Weekly => NextWeekly(schedule, now, timeZone),
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
            ScheduleKind.Daily => LatestDaily(schedule, now, timeZone),
            ScheduleKind.Weekly => LatestWeekly(schedule, now, timeZone),
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

    private static DateTimeOffset? NextDaily(
        TaskSchedule schedule,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        if (schedule.TimeOfDay is not { } time)
            return null;

        DateOnly date = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        for (int dayOffset = 0; dayOffset <= 3; dayOffset++)
        {
            DateTimeOffset candidate = LocalAt(date.AddDays(dayOffset), time, timeZone);
            if (candidate > now)
                return candidate;
        }

        return null;
    }

    private static DateTimeOffset? LatestDaily(
        TaskSchedule schedule,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        if (schedule.TimeOfDay is not { } time)
            return null;

        DateOnly date = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        for (int dayOffset = 0; dayOffset >= -3; dayOffset--)
        {
            DateTimeOffset candidate = LocalAt(date.AddDays(dayOffset), time, timeZone);
            if (candidate <= now)
                return candidate;
        }

        return null;
    }

    private static DateTimeOffset? NextWeekly(
        TaskSchedule schedule,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        if (schedule.TimeOfDay is not { } time || schedule.DaysOfWeek.Count == 0)
            return null;

        DateOnly date = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        for (int dayOffset = 0; dayOffset <= 8; dayOffset++)
        {
            DateOnly candidateDate = date.AddDays(dayOffset);
            if (!schedule.DaysOfWeek.Contains(candidateDate.DayOfWeek))
                continue;

            DateTimeOffset candidate = LocalAt(candidateDate, time, timeZone);
            if (candidate > now)
                return candidate;
        }

        return null;
    }

    private static DateTimeOffset? LatestWeekly(
        TaskSchedule schedule,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        if (schedule.TimeOfDay is not { } time || schedule.DaysOfWeek.Count == 0)
            return null;

        DateOnly date = DateOnly.FromDateTime(
            TimeZoneInfo.ConvertTime(now, timeZone).DateTime);
        for (int dayOffset = 0; dayOffset >= -8; dayOffset--)
        {
            DateOnly candidateDate = date.AddDays(dayOffset);
            if (!schedule.DaysOfWeek.Contains(candidateDate.DayOfWeek))
                continue;

            DateTimeOffset candidate = LocalAt(candidateDate, time, timeZone);
            if (candidate <= now)
                return candidate;
        }

        return null;
    }

    private static DateTimeOffset LocalAt(
        DateOnly date,
        TimeOnly time,
        TimeZoneInfo timeZone)
    {
        DateTime local = date.ToDateTime(time, DateTimeKind.Unspecified);
        for (int minute = 0; minute < 180 && timeZone.IsInvalidTime(local); minute++)
            local = local.AddMinutes(1);

        if (timeZone.IsInvalidTime(local))
            local = local.AddHours(3);

        TimeSpan offset;
        if (timeZone.IsAmbiguousTime(local))
        {
            offset = timeZone.GetAmbiguousTimeOffsets(local).MaxBy(value => value);
        }
        else
        {
            offset = timeZone.GetUtcOffset(local);
        }

        return new DateTimeOffset(local, offset);
    }
}
