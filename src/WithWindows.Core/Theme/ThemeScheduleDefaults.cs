using System;

namespace WithWindows.Core.Theme;

/// <summary>
/// 定位不可用时使用的固定亮暗切换时间。仅用于降级，不覆盖成功获取的坐标；
/// 日出日落只需要城市级位置，默认时间保证自动切换仍然可用。
/// </summary>
public static class ThemeScheduleDefaults
{
    /// <summary>默认切换到亮色模式的时间：07:00。</summary>
    public static readonly TimeSpan LightStart = TimeSpan.FromHours(7);

    /// <summary>默认切换到暗色模式的时间：19:00。</summary>
    public static readonly TimeSpan DarkStart = TimeSpan.FromHours(19);

    /// <summary>用于界面提示的默认时间范围。</summary>
    public static string DisplayRange =>
        $"{LightStart.Hours:00}:{LightStart.Minutes:00} / "
        + $"{DarkStart.Hours:00}:{DarkStart.Minutes:00}";

    /// <summary>当前时刻按默认时间应处于的主题。</summary>
    public static ThemeMode TargetAt(DateTimeOffset now, TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        TimeSpan localTime = TimeZoneInfo.ConvertTime(now, timeZone).TimeOfDay;
        return localTime >= LightStart && localTime < DarkStart
            ? ThemeMode.Light
            : ThemeMode.Dark;
    }

    /// <summary>返回严格晚于现在的最近一次默认切换。</summary>
    public static ThemeScheduleOccurrence NextAfter(
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        DateTime localNow = TimeZoneInfo.ConvertTime(now, timeZone).DateTime;
        DateOnly date = DateOnly.FromDateTime(localNow);

        DateTimeOffset light = At(date, LightStart, timeZone);
        DateTimeOffset dark = At(date, DarkStart, timeZone);
        DateTimeOffset? next = null;
        bool nextIsLight = false;

        if (light > now)
        {
            next = light;
            nextIsLight = true;
        }

        if (dark > now && (next is null || dark < next.Value))
        {
            next = dark;
            nextIsLight = false;
        }

        if (next is null)
        {
            next = At(date.AddDays(1), LightStart, timeZone);
            nextIsLight = true;
        }

        return new ThemeScheduleOccurrence(next.Value, nextIsLight);
    }

    private static DateTimeOffset At(
        DateOnly date,
        TimeSpan time,
        TimeZoneInfo timeZone)
    {
        DateTime local = date.ToDateTime(
            TimeOnly.FromTimeSpan(time),
            DateTimeKind.Unspecified);
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local));
    }
}
