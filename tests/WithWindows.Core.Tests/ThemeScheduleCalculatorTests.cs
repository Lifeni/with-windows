using WithWindows.Core.Theme;

namespace WithWindows.Core.Tests;

public sealed class ThemeScheduleCalculatorTests
{
    private const double BeijingLatitude = 39.9042;
    private const double BeijingLongitude = 116.4074;
    private const double TromsoLatitude = 69.6492;
    private const double TromsoLongitude = 18.9553;

    private static readonly TimeZoneInfo Utc8 =
        TimeZoneInfo.CreateCustomTimeZone(
            "UTC+8",
            TimeSpan.FromHours(8),
            "UTC+8",
            "UTC+8");

    private static readonly TimeZoneInfo TromsoSummer =
        TimeZoneInfo.CreateCustomTimeZone(
            "UTC+2",
            TimeSpan.FromHours(2),
            "UTC+2",
            "UTC+2");

    private static readonly TimeZoneInfo TromsoWinter =
        TimeZoneInfo.CreateCustomTimeZone(
            "UTC+1",
            TimeSpan.FromHours(1),
            "UTC+1",
            "UTC+1");

    [Theory]
    [InlineData("2026-08-15", "05:25", "19:12")]
    [InlineData("2026-12-21", "07:32", "16:52")]
    [InlineData("2026-03-20", "06:19", "18:26")]
    public void SunTimes_北京参考值(string date, string sunrise, string sunset)
    {
        ThemeDayTimes times = SunTimes.GetDayTimes(
            DateOnly.Parse(date),
            BeijingLatitude,
            BeijingLongitude,
            Utc8);

        Assert.NotNull(times.Sunrise);
        Assert.NotNull(times.Sunset);
        Assert.Null(times.AllDayMode);
        Assert.InRange(
            times.Sunrise.Value.TimeOfDay - TimeSpan.Parse(sunrise),
            -TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
        Assert.InRange(
            times.Sunset.Value.TimeOfDay - TimeSpan.Parse(sunset),
            -TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void SunTimes_极昼与极夜()
    {
        ThemeDayTimes summer = SunTimes.GetDayTimes(
            new DateOnly(2026, 6, 21),
            TromsoLatitude,
            TromsoLongitude,
            TromsoSummer);
        ThemeDayTimes winter = SunTimes.GetDayTimes(
            new DateOnly(2026, 12, 21),
            TromsoLatitude,
            TromsoLongitude,
            TromsoWinter);

        Assert.Null(summer.Sunrise);
        Assert.Null(summer.Sunset);
        Assert.Equal(ThemeMode.Light, summer.AllDayMode);
        Assert.Null(winter.Sunrise);
        Assert.Null(winter.Sunset);
        Assert.Equal(ThemeMode.Dark, winter.AllDayMode);
    }

    [Theory]
    [InlineData(4, ThemeMode.Dark)]
    [InlineData(12, ThemeMode.Light)]
    [InlineData(20, ThemeMode.Dark)]
    public void TargetAt_按日出日落判断当前主题(int hour, ThemeMode expected)
    {
        DateTimeOffset now = Local(2026, 8, 15, hour);

        ThemeMode? target = ThemeScheduleCalculator.TargetAt(
            now,
            BeijingLatitude,
            BeijingLongitude,
            Utc8);

        Assert.Equal(expected, target);
    }

    [Fact]
    public void TargetAt_极昼极夜返回全天主题()
    {
        Assert.Equal(
            ThemeMode.Light,
            ThemeScheduleCalculator.TargetAt(
                new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(2)),
                TromsoLatitude,
                TromsoLongitude,
                TromsoSummer));
        Assert.Equal(
            ThemeMode.Dark,
            ThemeScheduleCalculator.TargetAt(
                new DateTimeOffset(2026, 12, 21, 12, 0, 0, TimeSpan.FromHours(1)),
                TromsoLatitude,
                TromsoLongitude,
                TromsoWinter));
    }

    [Fact]
    public void NextAfter_日出前返回日出亮色()
    {
        ThemeScheduleDay day = ThemeScheduleCalculator.GetDay(
            new DateOnly(2026, 8, 15),
            BeijingLatitude,
            BeijingLongitude,
            Utc8);
        DateTimeOffset beforeSunrise = day.Sunrise!.Value.AddMinutes(-1);

        ThemeScheduleOccurrence? occurrence = ThemeScheduleCalculator.NextAfter(
            beforeSunrise,
            BeijingLatitude,
            BeijingLongitude,
            Utc8);

        Assert.NotNull(occurrence);
        Assert.True(occurrence.Value.ToLight);
        Assert.Equal(day.Sunrise, occurrence.Value.At);
    }

    [Fact]
    public void NextAfter_白天返回日落暗色()
    {
        DateTimeOffset noon = Local(2026, 8, 15, 12);

        ThemeScheduleOccurrence? occurrence = ThemeScheduleCalculator.NextAfter(
            noon,
            BeijingLatitude,
            BeijingLongitude,
            Utc8);

        Assert.NotNull(occurrence);
        Assert.False(occurrence.Value.ToLight);
        Assert.Equal(Local(2026, 8, 15, 19, 12), occurrence.Value.At, TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void NextAfter_日落后返回次日出亮色()
    {
        DateTimeOffset afterSunset = Local(2026, 8, 15, 20);
        ThemeScheduleDay tomorrow = ThemeScheduleCalculator.GetDay(
            new DateOnly(2026, 8, 16),
            BeijingLatitude,
            BeijingLongitude,
            Utc8);

        ThemeScheduleOccurrence? occurrence = ThemeScheduleCalculator.NextAfter(
            afterSunset,
            BeijingLatitude,
            BeijingLongitude,
            Utc8);

        Assert.NotNull(occurrence);
        Assert.True(occurrence.Value.ToLight);
        Assert.Equal(tomorrow.Sunrise, occurrence.Value.At);
    }

    [Fact]
    public void NextAfter_连续极昼无切换点()
    {
        ThemeScheduleOccurrence? occurrence = ThemeScheduleCalculator.NextAfter(
            new DateTimeOffset(2026, 6, 21, 12, 0, 0, TimeSpan.FromHours(2)),
            TromsoLatitude,
            TromsoLongitude,
            TromsoSummer);

        Assert.Null(occurrence);
    }

    [Fact]
    public void SunTimes_坐标越界_抛异常()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SunTimes.GetDayTimes(new DateOnly(2026, 8, 15), 91, 116, Utc8));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            SunTimes.GetDayTimes(new DateOnly(2026, 8, 15), 39, 181, Utc8));
    }

    private static DateTimeOffset Local(int year, int month, int day, int hour)
        => Local(year, month, day, hour, 0);

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute)
        => new(year, month, day, hour, minute, 0, TimeSpan.FromHours(8));
}
