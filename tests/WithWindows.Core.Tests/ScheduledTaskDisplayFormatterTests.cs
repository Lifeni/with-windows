using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduledTaskDisplayFormatterTests
{
    [Fact]
    public void ScheduleText_手动任务显示为手动运行()
    {
        var task = new ScheduledTask
        {
            Schedule = new TaskSchedule
            {
                Kind = ScheduleKind.Manual,
            },
        };

        Assert.Equal("手动运行", ScheduledTaskDisplayFormatter.ScheduleText(task));
    }

    [Fact]
    public void ScheduleText_单次任务显示本地日期和时间()
    {
        DateTime local = new(2026, 11, 10, 18, 5, 0, DateTimeKind.Unspecified);
        DateTimeOffset runAt = new(local);
        var task = new ScheduledTask
        {
            Schedule = new TaskSchedule
            {
                Kind = ScheduleKind.Once,
                OneTimeRunAt = runAt,
            },
        };

        Assert.Equal(
            $"单次 {runAt.ToLocalTime():yyyy年M月d日 HH:mm}",
            ScheduledTaskDisplayFormatter.ScheduleText(task));
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
    public void IsExpired_未消费且时间已过的单次任务返回真()
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
