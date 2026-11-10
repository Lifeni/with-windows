using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduleCalculatorTests
{
    private static readonly TimeZoneInfo Utc8 =
        TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

    [Fact]
    public void NextAfter_手动任务返回空()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Manual,
        };

        Assert.Null(ScheduleCalculator.NextAfter(schedule, Local(8, 0), Utc8));
    }

    [Fact]
    public void NextAfter_未消费单次任务返回原时间()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Once,
            OneTimeRunAt = Local(12, 0),
        };

        Assert.Equal(
            Local(12, 0),
            ScheduleCalculator.NextAfter(schedule, Local(8, 0), Utc8));
    }

    [Fact]
    public void NextAfter_已消费单次任务返回空()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Once,
            OneTimeRunAt = Local(12, 0),
            OneTimeConsumedAt = Local(12, 0).AddMilliseconds(20),
        };

        Assert.Null(ScheduleCalculator.NextAfter(schedule, Local(8, 0), Utc8));
    }

    [Fact]
    public void LatestAtOrBefore_过期未消费单次任务返回原时间()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Once,
            OneTimeRunAt = Local(8, 0),
        };

        Assert.Equal(
            Local(8, 0),
            ScheduleCalculator.LatestAtOrBefore(schedule, Local(9, 0), Utc8));
    }

    [Fact]
    public void LatestAtOrBefore_手动任务返回空()
    {
        Assert.Null(ScheduleCalculator.LatestAtOrBefore(
            new TaskSchedule { Kind = ScheduleKind.Manual },
            Local(9, 0),
            Utc8));
    }

    [Fact]
    public void NextAfter_每天任务返回今天稍后的时间()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Daily,
            TimeOfDay = new TimeOnly(9, 30),
        };

        Assert.Equal(
            Local(9, 30),
            ScheduleCalculator.NextAfter(schedule, Local(8, 0), Utc8));
    }

    [Fact]
    public void NextAfter_每周任务返回下一个选中的星期()
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
    public void NextAfter_每月31日跳过没有31日的月份()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Monthly,
            TimeOfDay = new TimeOnly(10, 0),
            DayOfMonth = 31,
        };

        DateTimeOffset next = ScheduleCalculator.NextAfter(
            schedule,
            new DateTimeOffset(2026, 2, 1, 9, 0, 0, TimeSpan.FromHours(8)),
            Utc8)!.Value;

        Assert.Equal(
            new DateTimeOffset(2026, 3, 31, 10, 0, 0, TimeSpan.FromHours(8)),
            next);
    }

    [Fact]
    public void LatestAtOrBefore_定期任务返回最近一次时间()
    {
        var schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Daily,
            TimeOfDay = new TimeOnly(9, 0),
        };

        Assert.Equal(
            Local(9, 0),
            ScheduleCalculator.LatestAtOrBefore(
                schedule,
                Local(9, 0).AddHours(3),
                Utc8));
    }

    private static DateTimeOffset Local(int hour, int minute)
        => new(2026, 11, 10, hour, minute, 0, TimeSpan.FromHours(8));
}
