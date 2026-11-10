using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduledTaskClaimPolicyTests
{
    private static readonly TimeZoneInfo Utc8 =
        TimeZoneInfo.CreateCustomTimeZone("UTC+8", TimeSpan.FromHours(8), "UTC+8", "UTC+8");

    [Fact]
    public void GetDueClaims_单次任务到点后认领()
    {
        ScheduledTask task = Once("task", Local(9, 30));

        ScheduledTaskOccurrence claim = Assert.Single(
            ScheduledTaskClaimPolicy.GetDueClaims(
                [task],
                Local(9, 30).AddMilliseconds(20),
                Utc8));

        Assert.Equal(Local(9, 30), claim.ScheduledFor);
    }

    [Fact]
    public void GetDueClaims_已完成认领的单次任务不重复返回()
    {
        ScheduledTask task = Once("task", Local(9, 30));
        task.LastHandledScheduledFor = Local(9, 30);

        Assert.Empty(ScheduledTaskClaimPolicy.GetDueClaims(
            [task],
            Local(10, 0),
            Utc8));
    }

    [Fact]
    public void GetDueClaims_同刻多个单次任务全部返回()
    {
        ScheduledTask first = Once("first", Local(9, 30));
        ScheduledTask second = Once("second", Local(9, 30));

        IReadOnlyList<ScheduledTaskOccurrence> claims =
            ScheduledTaskClaimPolicy.GetDueClaims(
                [first, second],
                Local(9, 30),
                Utc8);

        Assert.Equal(2, claims.Count);
        Assert.Equal(["first", "second"], claims.Select(claim => claim.TaskId));
    }

    [Fact]
    public void GetDueClaims_手动任务和已消费单次任务不返回()
    {
        ScheduledTask manual = Once("manual", Local(9, 30));
        manual.Schedule.Kind = ScheduleKind.Manual;
        manual.Schedule.OneTimeRunAt = null;

        ScheduledTask consumed = Once("consumed", Local(9, 30));
        consumed.Schedule.OneTimeConsumedAt = Local(9, 30);

        Assert.Empty(ScheduledTaskClaimPolicy.GetDueClaims(
            [manual, consumed],
            Local(10, 0),
            Utc8));
    }

    [Fact]
    public void GetDueClaims_任务创建前的时间不补跑()
    {
        ScheduledTask task = Once("task", Local(9, 0));
        task.CreatedAt = Local(15, 0);

        Assert.Empty(ScheduledTaskClaimPolicy.GetDueClaims(
            [task],
            Local(15, 5),
            Utc8));
    }

    [Fact]
    public void GetDueClaims_启动时间之前的时间不补跑()
    {
        ScheduledTask task = Once("task", Local(9, 0));
        task.CreatedAt = Local(9, 0).AddDays(-2);

        Assert.Empty(ScheduledTaskClaimPolicy.GetDueClaims(
            [task],
            Local(10, 0),
            Utc8,
            notBefore: Local(10, 0)));
    }

    [Fact]
    public void GetDueClaims_启动时间之后的迟到时间仍会补跑()
    {
        ScheduledTask task = Once("task", Local(9, 0));
        task.CreatedAt = Local(9, 0).AddDays(-2);

        ScheduledTaskOccurrence occurrence = Assert.Single(
            ScheduledTaskClaimPolicy.GetDueClaims(
                [task],
                Local(10, 0),
                Utc8,
                notBefore: Local(8, 0)));

        Assert.Equal(Local(9, 0), occurrence.ScheduledFor);
    }

    private static ScheduledTask Once(string id, DateTimeOffset runAt) => new()
    {
        Id = id,
        Name = id,
        ScheduleRevision = 1,
        Schedule = new TaskSchedule
        {
            Kind = ScheduleKind.Once,
            OneTimeRunAt = runAt,
        },
        ScriptText = "print('ok')",
    };

    private static DateTimeOffset Local(int hour, int minute)
        => new(2026, 11, 10, hour, minute, 0, TimeSpan.FromHours(8));
}
