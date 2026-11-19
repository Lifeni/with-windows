using WithWindows.Core.Theme;

namespace WithWindows.Core.Tests;

public sealed class ThemeScheduleDefaultsTests
{
    private static readonly TimeZoneInfo Utc8 =
        TimeZoneInfo.CreateCustomTimeZone(
            "UTC+8",
            TimeSpan.FromHours(8),
            "UTC+8",
            "UTC+8");

    [Theory]
    [InlineData(6, 30, ThemeMode.Dark)]
    [InlineData(7, 30, ThemeMode.Light)]
    [InlineData(19, 30, ThemeMode.Dark)]
    public void TargetAt_按默认时间判断当前主题(
        int hour,
        int minute,
        ThemeMode expected)
    {
        ThemeMode target = ThemeScheduleDefaults.TargetAt(
            Local(2026, 11, 18, hour, minute),
            Utc8);

        Assert.Equal(expected, target);
    }

    [Fact]
    public void NextAfter_日出前返回当天默认亮色时间()
    {
        ThemeScheduleOccurrence next = ThemeScheduleDefaults.NextAfter(
            Local(2026, 11, 18, 6, 30),
            Utc8);

        Assert.True(next.ToLight);
        Assert.Equal(Local(2026, 11, 18, 7, 0), next.At);
    }

    [Fact]
    public void NextAfter_白天返回当天默认暗色时间()
    {
        ThemeScheduleOccurrence next = ThemeScheduleDefaults.NextAfter(
            Local(2026, 11, 18, 12, 0),
            Utc8);

        Assert.False(next.ToLight);
        Assert.Equal(Local(2026, 11, 18, 19, 0), next.At);
    }

    [Fact]
    public void NextAfter_夜间返回次日默认亮色时间()
    {
        ThemeScheduleOccurrence next = ThemeScheduleDefaults.NextAfter(
            Local(2026, 11, 18, 20, 0),
            Utc8);

        Assert.True(next.ToLight);
        Assert.Equal(Local(2026, 11, 19, 7, 0), next.At);
    }

    [Fact]
    public void DisplayRange_为固定默认时间()
        => Assert.Equal("07:00 / 19:00", ThemeScheduleDefaults.DisplayRange);

    private static DateTimeOffset Local(
        int year,
        int month,
        int day,
        int hour,
        int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));
}
