using WithWindows.Actions;
using WithWindows.Config;

namespace WithWindows.Tests;

public class SunTimesTests
{
    // 参考值由 NOAA 算法独立实现（Python）计算，±5 分钟容差覆盖双精度舍入差异
    [Theory]
    [InlineData("2026-08-15", "05:25", "19:12")] // 北京 夏
    [InlineData("2026-12-21", "07:32", "16:52")] // 北京 冬
    [InlineData("2026-03-20", "06:19", "18:26")] // 北京 春分
    public void GetDayTimes_Beijing_MatchesReference(string date, string rise, string set)
    {
        var times = SunTimes.GetDayTimes(DateTime.Parse(date), 39.9042, 116.4074, TimeSpan.FromHours(8));

        Assert.NotNull(times.Sunrise);
        Assert.NotNull(times.Sunset);
        Assert.Null(times.AllDay);
        Assert.InRange(times.Sunrise.Value.TimeOfDay - TimeSpan.Parse(rise),
            -TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        Assert.InRange(times.Sunset.Value.TimeOfDay - TimeSpan.Parse(set),
            -TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void GetDayTimes_PolarDay_ReturnsAllDayLight()
    {
        // 特罗姆瑟 6 月：极昼，太阳不落
        var times = SunTimes.GetDayTimes(DateTime.Parse("2026-06-21"), 69.6492, 18.9553, TimeSpan.FromHours(2));

        Assert.Null(times.Sunrise);
        Assert.Null(times.Sunset);
        Assert.Equal("light", times.AllDay);
    }

    [Fact]
    public void GetDayTimes_PolarNight_ReturnsAllDayDark()
    {
        // 特罗姆瑟 12 月：极夜，太阳不升
        var times = SunTimes.GetDayTimes(DateTime.Parse("2026-12-21"), 69.6492, 18.9553, TimeSpan.FromHours(1));

        Assert.Null(times.Sunrise);
        Assert.Null(times.Sunset);
        Assert.Equal("dark", times.AllDay);
    }

    [Fact]
    public void GetDayTimes_SummerSunriseEarlierAndSunsetLaterThanWinter()
    {
        var summer = SunTimes.GetDayTimes(DateTime.Parse("2026-08-15"), 39.9042, 116.4074, TimeSpan.FromHours(8));
        var winter = SunTimes.GetDayTimes(DateTime.Parse("2026-12-21"), 39.9042, 116.4074, TimeSpan.FromHours(8));

        Assert.True(summer.Sunrise!.Value.TimeOfDay < winter.Sunrise!.Value.TimeOfDay);
        Assert.True(summer.Sunset!.Value.TimeOfDay > winter.Sunset!.Value.TimeOfDay);
    }
}

public class AutoThemeSchedulerTests
{
    private static DateTime At(string time) => DateTime.Parse($"2026-08-15 {time}");

    [Theory]
    [InlineData("05:00", "dark")]  // 日出前
    [InlineData("12:00", "light")] // 日间
    [InlineData("19:00", "dark")]  // 日落后
    [InlineData("00:30", "dark")]  // 午夜
    public void TargetFor_DaylightWindow(string now, string expected)
    {
        DateTime? rise = At("06:00"), set = At("18:00");

        Assert.Equal(expected, AutoThemeScheduler.TargetFor(At(now), rise, set, null));
    }

    [Fact]
    public void TargetFor_PolarDay_AlwaysLight()
    {
        Assert.Equal("light", AutoThemeScheduler.TargetFor(At("12:00"), null, null, "light"));
        Assert.Equal("light", AutoThemeScheduler.TargetFor(At("00:00"), null, null, "light"));
    }

    [Fact]
    public void TargetFor_UnknownTimes_ReturnsNull()
    {
        Assert.Null(AutoThemeScheduler.TargetFor(At("12:00"), null, null, null));
    }

    [Theory]
    [InlineData("04:00", "06:00")] // 日出前 → 今日日出
    [InlineData("12:00", "18:00")] // 日间 → 今日日落
    public void NextSwitch_BeforeSunset_PicksNextEvent(string now, string expected)
    {
        DateTime? rise = At("06:00"), set = At("18:00");

        Assert.Equal(At(expected), AutoThemeScheduler.NextSwitch(At(now), rise, set, null));
    }

    [Fact]
    public void NextSwitch_AfterSunset_PicksTomorrowSunrise()
    {
        DateTime? rise = At("06:00"), set = At("18:00");
        DateTime tomorrowSunrise = At("06:00").AddDays(1);

        Assert.Equal(tomorrowSunrise, AutoThemeScheduler.NextSwitch(At("19:00"), rise, set, tomorrowSunrise));
    }

    [Fact]
    public void NextSwitch_NoEvents_ReturnsNull()
    {
        Assert.Null(AutoThemeScheduler.NextSwitch(At("12:00"), null, null, null));
    }
}

public class AutoThemeSettingsTests
{
    [Fact]
    public void FromArgs_Coordinates_Valid()
    {
        var args = MiniJson.Parse("""{ "latitude": "39.9042", "longitude": "116.4074", "offset_minutes": "0" }""");

        var settings = AutoThemeSettings.FromArgs(args);

        Assert.True(settings.HasCoordinates);
        Assert.Null(settings.Validate());
        Assert.Equal(39.9042, settings.Latitude!.Value, 5);
    }

    [Fact]
    public void FromArgs_FixedTimes_Valid()
    {
        var args = MiniJson.Parse("""{ "sunrise": "06:30", "sunset": "18:30" }""");

        var settings = AutoThemeSettings.FromArgs(args);

        Assert.True(settings.HasFixedTimes);
        Assert.Null(settings.Validate());
    }

    [Fact]
    public void FromArgs_MissingConfig_FallsBackToDefault()
    {
        var settings = AutoThemeSettings.FromArgs(null);

        Assert.True(settings.HasCoordinates);
        Assert.Null(settings.Validate());
        Assert.Equal(AutoThemeSettings.DefaultLatitude, settings.Latitude!.Value, 5);
        Assert.Equal(AutoThemeSettings.DefaultLongitude, settings.Longitude!.Value, 5);
    }

    [Fact]
    public void FromArgs_MissingConfig_ComputesDefaultDayTimes()
    {
        // 时区固定北京时间（UTC+8）：日出/日落时刻与机器时区无关，CI（UTC）与本地断言一致。
        // 参考值由 NOAA 算法独立实现（Python）计算：2026-08-15 日出 05:28、日落 19:03
        var settings = AutoThemeSettings.FromArgs(null);

        var (rise, set, allDay) = settings.GetDayTimes(DateTime.Parse("2026-08-15 10:00"));

        Assert.NotNull(rise);
        Assert.NotNull(set);
        Assert.Null(allDay);
        Assert.InRange(rise!.Value.TimeOfDay - TimeSpan.Parse("05:28"),
            -TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
        Assert.InRange(set!.Value.TimeOfDay - TimeSpan.Parse("19:03"),
            -TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void FromArgs_LatitudeOutOfRange_Invalid()
    {
        var args = MiniJson.Parse("""{ "latitude": "100", "longitude": "116.4" }""");

        Assert.NotNull(AutoThemeSettings.FromArgs(args).Validate());
    }

    [Fact]
    public void FromArgs_SunriseAfterSunset_Invalid()
    {
        var args = MiniJson.Parse("""{ "sunrise": "18:30", "sunset": "06:30" }""");

        Assert.NotNull(AutoThemeSettings.FromArgs(args).Validate());
    }

    [Fact]
    public void FromArgs_OnlyOneCoordinate_FallsBackToDefault()
    {
        // 只给 latitude：不构成有效坐标对，回退内置默认坐标（不报错，开箱即用）
        var args = MiniJson.Parse("""{ "latitude": "39.9" }""");

        var settings = AutoThemeSettings.FromArgs(args);

        Assert.True(settings.HasCoordinates);
        Assert.Null(settings.Validate());
        Assert.Equal(AutoThemeSettings.DefaultLatitude, settings.Latitude!.Value, 5);
    }

    [Fact]
    public void GetDayTimes_FixedTimes_UsesLocalDate()
    {
        var settings = AutoThemeSettings.FromArgs(MiniJson.Parse("""{ "sunrise": "06:30", "sunset": "18:30" }"""));

        var (rise, set, allDay) = settings.GetDayTimes(DateTime.Parse("2026-08-15 10:00"));

        Assert.Equal(DateTime.Parse("2026-08-15 06:30"), rise);
        Assert.Equal(DateTime.Parse("2026-08-15 18:30"), set);
        Assert.Null(allDay);
    }

    [Fact]
    public void GetDayTimes_FixedTimes_AppliesOffset()
    {
        var settings = AutoThemeSettings.FromArgs(
            MiniJson.Parse("""{ "sunrise": "06:00", "sunset": "18:00", "offset_minutes": "30" }"""));

        var (rise, set, _) = settings.GetDayTimes(DateTime.Parse("2026-08-15 10:00"));

        Assert.Equal(DateTime.Parse("2026-08-15 06:30"), rise);
        Assert.Equal(DateTime.Parse("2026-08-15 18:30"), set);
    }
}
