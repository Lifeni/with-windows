using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduledTaskDisplayFormatterTests
{
    [Fact]
    public void ScheduleText_每天显示本地时分()
    {
        var task = new ScheduledTask
        {
            Schedule = new TaskSchedule
            {
                Kind = ScheduleKind.Daily,
                TimeOfDay = new TimeOnly(9, 30),
            },
        };

        Assert.Equal("每天 09:30", ScheduledTaskDisplayFormatter.ScheduleText(task));
    }

    [Fact]
    public void ScheduleText_每周按星期顺序显示()
    {
        var task = new ScheduledTask
        {
            Schedule = new TaskSchedule
            {
                Kind = ScheduleKind.Weekly,
                TimeOfDay = new TimeOnly(18, 5),
                DaysOfWeek = [DayOfWeek.Friday, DayOfWeek.Monday],
            },
        };

        Assert.Equal("每周一、周五 18:05", ScheduledTaskDisplayFormatter.ScheduleText(task));
    }

    [Fact]
    public void RunStatusText_返回中文状态()
    {
        Assert.Equal(
            "成功",
            ScheduledTaskDisplayFormatter.RunStatusText(
                ScheduledTaskRunStatus.Succeeded));
        Assert.Equal(
            "运行中",
            ScheduledTaskDisplayFormatter.RunStatusText(
                ScheduledTaskRunStatus.Running));
    }

    [Fact]
    public void IsExpired_未消费且时间已过的一次性任务返回真()
    {
        var task = new ScheduledTask
        {
            Schedule = new TaskSchedule
            {
                Kind = ScheduleKind.Once,
                OneTimeRunAt = new DateTimeOffset(
                    2026,
                    11,
                    10,
                    9,
                    0,
                    0,
                    TimeSpan.FromHours(8)),
            },
        };

        Assert.True(ScheduledTaskDisplayFormatter.IsExpired(
            task,
            new DateTimeOffset(
                2026,
                11,
                10,
                10,
                0,
                0,
                TimeSpan.FromHours(8))));
    }
}
