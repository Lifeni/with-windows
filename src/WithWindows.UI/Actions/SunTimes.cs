namespace WithWindows.Actions;

/// <summary>
/// 一天内亮/暗切换的关键时间点（本地时间）。
/// 正常情况 Sunrise/Sunset 有值、AllDay 为 null；极昼（太阳不落）AllDay="light"、
/// 极夜（太阳不升）AllDay="dark"，此时两项时间为 null。
/// </summary>
internal readonly struct DayLightTimes
{
    public readonly DateTime? Sunrise;
    public readonly DateTime? Sunset;
    public readonly string? AllDay;

    public DayLightTimes(DateTime? sunrise, DateTime? sunset, string? allDay)
    {
        Sunrise = sunrise;
        Sunset = sunset;
        AllDay = allDay;
    }
}

/// <summary>
/// NOAA 日出日落算法（edwilliams.org/sunrise_sunset_algorithm.htm）：给定日期、经纬度与
/// 时区偏移，返回当地时间的日出/日落时刻。官方天顶角 90.8333°（含大气折射与太阳半径修正），
/// 中纬度误差约几分钟，符合"大概"精度需求。纯函数，便于测试。
/// </summary>
internal static class SunTimes
{
    private const double Zenith = 90.8333;

    public static DayLightTimes GetDayTimes(DateTime localDate, double latitude, double longitude, TimeSpan utcOffset)
    {
        int dayOfYear = localDate.DayOfYear;
        double lngHour = longitude / 15.0;

        var (sunrise, riseCosH) = CalcEvent(localDate, dayOfYear, lngHour, 6, latitude, utcOffset, isRise: true);
        var (sunset, setCosH) = CalcEvent(localDate, dayOfYear, lngHour, 18, latitude, utcOffset, isRise: false);

        if (sunrise is null && sunset is null)
        {
            // 极夜（cosH > 1，太阳不升）整天暗；极昼（cosH < -1，太阳不落）整天亮
            return new DayLightTimes(null, null, riseCosH > 1.0 ? "dark" : "light");
        }
        return new DayLightTimes(sunrise, sunset, null);
    }

    private static (DateTime? Time, double CosH) CalcEvent(
        DateTime localDate, int dayOfYear, double lngHour, double approxHour,
        double latitude, TimeSpan utcOffset, bool isRise)
    {
        // 1-2. 当年第几天 + 近似时刻 t（日出 6h / 日落 18h）
        double t = dayOfYear + ((approxHour - lngHour) / 24.0);

        // 3. 平近点角
        double m = 0.9856 * t - 3.289;

        // 4. 太阳黄经（归一到 [0,360)）
        double l = NormalizeDeg(m + 1.916 * Math.Sin(Deg2Rad(m)) + 0.020 * Math.Sin(Deg2Rad(2 * m)) + 282.634);

        // 5. 赤经（atan 多值性：与黄经同象限，转小时）
        double ra = NormalizeDeg(Rad2Deg(Math.Atan(0.91764 * Math.Tan(Deg2Rad(l)))));
        double lQuadrant = Math.Floor(l / 90.0) * 90.0;
        double raQuadrant = Math.Floor(ra / 90.0) * 90.0;
        ra = NormalizeDeg(ra + (lQuadrant - raQuadrant)) / 15.0;

        // 6. 赤纬
        double sinDec = 0.39782 * Math.Sin(Deg2Rad(l));
        double cosDec = Math.Cos(Math.Asin(sinDec));

        // 7. 时角余弦；越界即极昼/极夜
        double cosH = (Math.Cos(Deg2Rad(Zenith)) - sinDec * Math.Sin(Deg2Rad(latitude)))
            / (cosDec * Math.Cos(Deg2Rad(latitude)));
        if (cosH > 1.0 || cosH < -1.0)
            return (null, cosH);

        // 8-10. 本地时角 → UTC → 本地时间（偏移由调用方传系统时区）
        double hourAngle = (isRise ? 360.0 - Rad2Deg(Math.Acos(cosH)) : Rad2Deg(Math.Acos(cosH))) / 15.0;
        double ut = Normalize24(hourAngle + ra - 0.06571 * t - 6.622 - lngHour);
        double local = Normalize24(ut + utcOffset.TotalHours);
        return (localDate.Date.AddHours(local), cosH);
    }

    private static double Deg2Rad(double d) => d * Math.PI / 180.0;

    private static double Rad2Deg(double r) => r * 180.0 / Math.PI;

    private static double NormalizeDeg(double d)
    {
        d %= 360.0;
        return d < 0 ? d + 360.0 : d;
    }

    private static double Normalize24(double h)
    {
        h %= 24.0;
        return h < 0 ? h + 24.0 : h;
    }
}
