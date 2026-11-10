using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduledTaskClaimPolicyTests
{
    private static readonly TimeZoneInfo Utc8 =
        TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

    [Fact]
    public void GetDueClaims_刷新恰好发生时认领今天的每日任务()
    {
        ScheduledTask task = Daily("task", new TimeOnly(9, 30));

        IReadOnlyList<ScheduledTaskOccurrence> claims =
            ScheduledTaskClaimPolicy.GetDueClaims(
                [task],
                Local(9, 30).AddMilliseconds(20),
                Utc8);

        ScheduledTaskOccurrence claim = Assert.Single(claims);
        Assert.Equal(Local(9, 30), claim.ScheduledFor);
    }

    [Fact]
    public void GetDueClaims_已完成认领的同occurrence不重复返回()
    {
        ScheduledTask task = Daily("task", new TimeOnly(9, 30));
        task.LastHandledScheduledFor = Local(9, 30);

        Assert.Empty(ScheduledTaskClaimPolicy.GetDueClaims(
            [task],
            Local(10, 0),
            Utc8));
    }

    [Fact]
    public void GetDueClaims_同刻多个启用任务全部返回()
    {
        ScheduledTask first = Daily("first", new TimeOnly(9, 30));
        ScheduledTask second = Daily("second", new TimeOnly(9, 30));

        IReadOnlyList<ScheduledTaskOccurrence> claims =
            ScheduledTaskClaimPolicy.GetDueClaims(
                [first, second],
                Local(9, 30),
                Utc8);

        Assert.Equal(2, claims.Count);
        Assert.Equal(["first", "second"], claims.Select(claim => claim.TaskId));
    }

    [Fact]
    public void GetDueClaims_停用任务和已消费一次性任务不返回()
    {
        ScheduledTask disabled = Daily("disabled", new TimeOnly(9, 30));
        disabled.Enabled = false;
        ScheduledTask consumed = Daily("consumed", new TimeOnly(9, 30));
        consumed.Schedule.Kind = ScheduleKind.Once;
        consumed.Schedule.OneTimeRunAt = Local(9, 30);
        consumed.Schedule.OneTimeConsumedAt = Local(9, 30);

        Assert.Empty(ScheduledTaskClaimPolicy.GetDueClaims(
            [disabled, consumed],
            Local(10, 0),
            Utc8));
    }

    [Fact]
    public void GetDueClaims_睡眠跨周期时只认领最近一次()
    {
        ScheduledTask task = Daily("task", new TimeOnly(9, 0));

        ScheduledTaskOccurrence claim = Assert.Single(
            ScheduledTaskClaimPolicy.GetDueClaims(
                [task],
                Local(9, 0).AddHours(27),
                Utc8));

        Assert.Equal(Local(9, 0).AddDays(1), claim.ScheduledFor);
    }

    [Fact]
    public void GetDueClaims_任务创建前的同类计划不补跑()
    {
        ScheduledTask task = Daily("task", new TimeOnly(9, 0));
        task.CreatedAt = Local(15, 0);

        Assert.Empty(ScheduledTaskClaimPolicy.GetDueClaims(
            [task],
            Local(15, 5),
            Utc8));
    }

    [Fact]
    public void GetDueClaims_启动时间之前的occurrence不补跑()
    {
        ScheduledTask task = Daily("task", new TimeOnly(9, 0));
        task.CreatedAt = Local(9, 0).AddDays(-2);

        Assert.Empty(ScheduledTaskClaimPolicy.GetDueClaims(
            [task],
            Local(10, 0),
            Utc8,
            notBefore: Local(10, 0)));
    }

    [Fact]
    public void GetDueClaims_启动时间之后的迟到occurrence仍会补跑()
    {
        ScheduledTask task = Daily("task", new TimeOnly(9, 0));
        task.CreatedAt = Local(9, 0).AddDays(-2);

        ScheduledTaskOccurrence occurrence = Assert.Single(
            ScheduledTaskClaimPolicy.GetDueClaims(
                [task],
                Local(10, 0),
                Utc8,
                notBefore: Local(8, 0)));

        Assert.Equal(Local(9, 0), occurrence.ScheduledFor);
    }

    private static ScheduledTask Daily(string id, TimeOnly time) => new()
    {
        Id = id,
        Name = id,
        Enabled = true,
        ScheduleRevision = 1,
        Schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Daily,
            TimeOfDay = time,
        },
        Source = new ScriptSource
        {
            Kind = ScriptSourceKind.Inline,
            ScriptText = "print('ok')",
        },
        PythonPath = @"C:\Python313\python.exe",
    };

    private static DateTimeOffset Local(int hour, int minute)
        => new(2026, 11, 10, hour, minute, 0, TimeSpan.FromHours(8));
}
