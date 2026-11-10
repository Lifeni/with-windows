using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduleCalculatorTests
{
    private static readonly TimeZoneInfo Utc8 =
        TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

    [Fact]
    public void NextAfter_每天计划返回今天稍后的时间()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Daily,
            TimeOfDay = new TimeOnly(9, 30),
        };

        DateTimeOffset next = ScheduleCalculator.NextAfter(
            schedule,
            Local(8, 0),
            Utc8)!.Value;

        Assert.Equal(Local(9, 30), next);
    }

    [Fact]
    public void NextAfter_每天计划在到点后返回下一天()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Daily,
            TimeOfDay = new TimeOnly(9, 30),
        };

        DateTimeOffset next = ScheduleCalculator.NextAfter(
            schedule,
            Local(9, 30).AddMilliseconds(20),
            Utc8)!.Value;

        Assert.Equal(Local(9, 30).AddDays(1), next);
    }

    [Fact]
    public void LatestAtOrBefore_睡眠跨天后返回最近一次每日计划()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Daily,
            TimeOfDay = new TimeOnly(9, 0),
        };

        DateTimeOffset latest = ScheduleCalculator.LatestAtOrBefore(
            schedule,
            Local(9, 0).AddHours(27),
            Utc8)!.Value;

        Assert.Equal(Local(9, 0).AddDays(1), latest);
    }

    [Fact]
    public void NextAfter_每周计划返回所选的下一星期()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Weekly,
            TimeOfDay = new TimeOnly(10, 0),
            DaysOfWeek = [DayOfWeek.Friday],
        };

        DateTimeOffset next = ScheduleCalculator.NextAfter(
            schedule,
            Local(9, 0),
            Utc8)!.Value;

        Assert.Equal(DayOfWeek.Friday, next.DayOfWeek);
        Assert.Equal(Local(9, 0).AddDays(3).Date.AddHours(10), next.DateTime);
    }

    [Fact]
    public void NextAfter_一次性任务在未消费时返回原时间()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Once,
            OneTimeRunAt = Local(12, 0),
        };

        Assert.Equal(Local(12, 0), ScheduleCalculator.NextAfter(schedule, Local(8, 0), Utc8));
    }

    [Fact]
    public void LatestAtOrBefore_一次性任务已过期时返回原时间()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Once,
            OneTimeRunAt = Local(8, 0),
        };

        Assert.Equal(Local(8, 0), ScheduleCalculator.LatestAtOrBefore(schedule, Local(9, 0), Utc8));
    }

    [Fact]
    public void LatestAtOrBefore_无效或空计划返回空()
    {
        Assert.Null(ScheduleCalculator.LatestAtOrBefore(
            new TaskSchedule { Kind = ScheduleKind.Weekly },
            Local(9, 0),
            Utc8));
    }

    private static DateTimeOffset Local(int hour, int minute)
        => new(2026, 11, 10, hour, minute, 0, TimeSpan.FromHours(8));
}
