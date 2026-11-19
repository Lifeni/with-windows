namespace WithWindows.Core.Theme;

/// <summary>某次定时主题切换：发生时间与目标是否为亮色。</summary>
public readonly record struct ThemeScheduleOccurrence(DateTimeOffset At, bool ToLight);

/// <summary>某一天的日出日落计划；极昼极夜时由 <see cref="AllDayMode"/> 指明全天主题。</summary>
public readonly record struct ThemeScheduleDay(
    DateOnly Date,
    DateTimeOffset? Sunrise,
    DateTimeOffset? Sunset,
    ThemeMode? AllDayMode);

/// <summary>按当前位置的日出日落计算当前主题与下一次切换。</summary>
public static class ThemeScheduleCalculator
{
    /// <summary>当前时刻按日出日落应处于的主题；缺少有效时间时返回 null。</summary>
    public static ThemeMode? TargetAt(
        DateTimeOffset now,
        double latitude,
        double longitude,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        ThemeScheduleDay day = GetDay(
            LocalDate(now, timeZone),
            latitude,
            longitude,
            timeZone);
        if (day.AllDayMode is { } allDayMode)
            return allDayMode;
        if (day.Sunrise is not { } sunrise || day.Sunset is not { } sunset)
            return null;

        if (now < sunrise)
            return ThemeMode.Dark;
        return now < sunset ? ThemeMode.Light : ThemeMode.Dark;
    }

    /// <summary>返回严格晚于现在的最近一次日出或日落；连续极昼极夜时返回 null。</summary>
    public static ThemeScheduleOccurrence? NextAfter(
        DateTimeOffset now,
        double latitude,
        double longitude,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        DateOnly start = LocalDate(now, timeZone);
        for (int dayOffset = 0; dayOffset <= 8; dayOffset++)
        {
            ThemeScheduleDay day = GetDay(
                start.AddDays(dayOffset),
                latitude,
                longitude,
                timeZone);
            if (day.AllDayMode is not null)
                continue;

            DateTimeOffset? next = null;
            if (day.Sunrise is { } sunrise && sunrise > now
                && (next is null || sunrise < next.Value))
            {
                next = sunrise;
            }

            if (day.Sunset is { } sunset && sunset > now
                && (next is null || sunset < next.Value))
            {
                next = sunset;
            }

            if (next is { } occurrence)
            {
                return new ThemeScheduleOccurrence(
                    occurrence,
                    ToLight: occurrence == day.Sunrise);
            }
        }

        return null;
    }

    /// <summary>计算某一天的日出、日落与极昼极夜状态。</summary>
    public static ThemeScheduleDay GetDay(
        DateOnly date,
        double latitude,
        double longitude,
        TimeZoneInfo timeZone)
    {
        ThemeDayTimes times = SunTimes.GetDayTimes(
            date,
            latitude,
            longitude,
            timeZone);
        return new ThemeScheduleDay(
            date,
            times.Sunrise,
            times.Sunset,
            times.AllDayMode);
    }

    private static DateOnly LocalDate(DateTimeOffset value, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, timeZone).DateTime);
}
