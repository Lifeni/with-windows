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

    private static DateTimeOffset Local(int hour, int minute)
        => new(2026, 11, 10, hour, minute, 0, TimeSpan.FromHours(8));
}
