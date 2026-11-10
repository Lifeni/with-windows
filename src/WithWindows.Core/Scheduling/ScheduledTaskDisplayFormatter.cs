namespace WithWindows.Core.Scheduling;

public static class ScheduledTaskDisplayFormatter
{
    public static string ScheduleText(ScheduledTask task)
    {
        TaskSchedule schedule = task.Schedule;
        return schedule.Kind switch
        {
            ScheduleKind.Once when schedule.OneTimeRunAt is { } runAt
                => $"单次 {runAt.ToLocalTime():yyyy年M月d日 HH:mm}",
            ScheduleKind.Manual => "手动运行",
            _ => "计划无效",
        };
    }

    public static string RunStatusText(ScheduledTaskRunStatus status)
        => status switch
        {
            ScheduledTaskRunStatus.Running => "运行中",
            ScheduledTaskRunStatus.Succeeded => "成功",
            ScheduledTaskRunStatus.Failed => "失败",
            ScheduledTaskRunStatus.TimedOut => "超时",
            ScheduledTaskRunStatus.Cancelled => "已取消",
            ScheduledTaskRunStatus.Skipped => "已跳过",
            ScheduledTaskRunStatus.Interrupted => "已中断",
            _ => "未知",
        };

    public static bool IsExpired(ScheduledTask task, DateTimeOffset now)
        => task.Schedule.Kind == ScheduleKind.Once
            && task.Schedule.OneTimeConsumedAt is null
            && task.Schedule.OneTimeRunAt is { } runAt
            && runAt <= now;
}
