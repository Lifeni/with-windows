namespace WithWindows.Core.Theme;

/// <summary>
/// 某一天的日出、日落与极昼极夜状态。正常日期两项时间都有值；
/// 极昼或极夜时两项时间都为空，由 <see cref="AllDayMode"/> 指明全天主题。
/// </summary>
public readonly record struct ThemeDayTimes(
    DateTimeOffset? Sunrise,
    DateTimeOffset? Sunset,
    ThemeMode? AllDayMode);

/// <summary>
/// NOAA 日出日落算法（edwilliams.org/sunrise_sunset_algorithm.htm）。
/// 天顶角 90.8333° 包含大气折射与太阳半径修正，误差约几分钟，足以驱动主题切换。
/// </summary>
public static class SunTimes
{
    private const double OfficialZenith = 90.8333;

    /// <summary>计算指定日期在给定经纬度、时区下的日出与日落。</summary>
    public static ThemeDayTimes GetDayTimes(
        DateOnly date,
        double latitude,
        double longitude,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        if (latitude is < -90 or > 90)
            throw new ArgumentOutOfRangeException(nameof(latitude));
        if (longitude is < -180 or > 180)
            throw new ArgumentOutOfRangeException(nameof(longitude));

        // 同一天内使用正午的 UTC 偏移，避免夏令时切换日拿错日期边界。
        DateTime localNoon = date.ToDateTime(
            TimeOnly.FromTimeSpan(TimeSpan.FromHours(12)),
            DateTimeKind.Unspecified);
        TimeSpan utcOffset = timeZone.GetUtcOffset(localNoon);

        int dayOfYear = date.DayOfYear;
        double longitudeHour = longitude / 15.0;
        (double? sunriseHour, double riseCosH) = CalculateEvent(
            dayOfYear,
            longitudeHour,
            approximateHour: 6,
            latitude,
            utcOffset,
            isRise: true);
        (double? sunsetHour, double setCosH) = CalculateEvent(
            dayOfYear,
            longitudeHour,
            approximateHour: 18,
            latitude,
            utcOffset,
            isRise: false);

        if (sunriseHour is null || sunsetHour is null)
        {
            // 极昼时太阳不落（cosH < -1），极夜时太阳不升（cosH > 1）。
            double cosH = sunriseHour is null ? riseCosH : setCosH;
            return new ThemeDayTimes(
                null,
                null,
                cosH > 1.0 ? ThemeMode.Dark : ThemeMode.Light);
        }

        DateTime localMidnight = date.ToDateTime(
            TimeOnly.MinValue,
            DateTimeKind.Unspecified);
        return new ThemeDayTimes(
            new DateTimeOffset(localMidnight.AddHours(sunriseHour.Value), utcOffset),
            new DateTimeOffset(localMidnight.AddHours(sunsetHour.Value), utcOffset),
            null);
    }

    private static (double? LocalHour, double CosH) CalculateEvent(
        int dayOfYear,
        double longitudeHour,
        double approximateHour,
        double latitude,
        TimeSpan utcOffset,
        bool isRise)
    {
        double t = dayOfYear + ((approximateHour - longitudeHour) / 24.0);
        double meanAnomaly = (0.9856 * t) - 3.289;
        double trueLongitude = NormalizeDegrees(
            meanAnomaly
            + (1.916 * Math.Sin(DegreesToRadians(meanAnomaly)))
            + (0.020 * Math.Sin(DegreesToRadians(2 * meanAnomaly)))
            + 282.634);

        double rightAscension = NormalizeDegrees(
            RadiansToDegrees(Math.Atan(0.91764 * Math.Tan(DegreesToRadians(trueLongitude)))));
        double longitudeQuadrant = Math.Floor(trueLongitude / 90.0) * 90.0;
        double rightAscensionQuadrant = Math.Floor(rightAscension / 90.0) * 90.0;
        rightAscension = NormalizeDegrees(
            rightAscension + longitudeQuadrant - rightAscensionQuadrant) / 15.0;

        double sinDeclination = 0.39782 * Math.Sin(DegreesToRadians(trueLongitude));
        double cosDeclination = Math.Cos(Math.Asin(sinDeclination));
        double cosHourAngle =
            (Math.Cos(DegreesToRadians(OfficialZenith))
             - (sinDeclination * Math.Sin(DegreesToRadians(latitude))))
            / (cosDeclination * Math.Cos(DegreesToRadians(latitude)));

        if (cosHourAngle is > 1.0 or < -1.0)
            return (null, cosHourAngle);

        double hourAngle = isRise
            ? (360.0 - RadiansToDegrees(Math.Acos(cosHourAngle))) / 15.0
            : RadiansToDegrees(Math.Acos(cosHourAngle)) / 15.0;
        double utcHours = Normalize24(
            hourAngle + rightAscension - (0.06571 * t) - 6.622 - longitudeHour);
        double localHours = Normalize24(utcHours + utcOffset.TotalHours);

        return (localHours, cosHourAngle);
    }

    private static double DegreesToRadians(double degrees)
        => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians)
        => radians * 180.0 / Math.PI;

    private static double NormalizeDegrees(double degrees)
    {
        degrees %= 360.0;
        return degrees < 0 ? degrees + 360.0 : degrees;
    }

    private static double Normalize24(double hours)
    {
        hours %= 24.0;
        return hours < 0 ? hours + 24.0 : hours;
    }
}
