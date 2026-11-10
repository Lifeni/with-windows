namespace WithWindows.Core.Scheduling;

public static class ScheduledTaskDisplayFormatter
{
    private static readonly DayOfWeek[] MondayFirst =
    [
        DayOfWeek.Monday,
        DayOfWeek.Tuesday,
        DayOfWeek.Wednesday,
        DayOfWeek.Thursday,
        DayOfWeek.Friday,
        DayOfWeek.Saturday,
        DayOfWeek.Sunday,
    ];

    public static string ScheduleText(ScheduledTask task)
    {
        TaskSchedule schedule = task.Schedule;
        return schedule.Kind switch
        {
            ScheduleKind.Once when schedule.OneTimeRunAt is { } runAt
                => $"单次 {runAt.ToLocalTime():yyyy年M月d日 HH:mm}",
            ScheduleKind.Daily when schedule.TimeOfDay is { } time
                => $"每天 {time:HH\\:mm}",
            ScheduleKind.Weekly when schedule.TimeOfDay is { } time
                => $"每{string.Join(
                    "、",
                    MondayFirst
                        .Where(schedule.DaysOfWeek.Contains)
                        .Select(WeekdayText))} {time:HH\\:mm}",
            ScheduleKind.Monthly when schedule.TimeOfDay is { } time
                && schedule.DayOfMonth is { } day
                => $"每月 {day} 日 {time:HH\\:mm}",
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

    public static string WeekdayText(DayOfWeek day)
        => day switch
        {
            DayOfWeek.Monday => "周一",
            DayOfWeek.Tuesday => "周二",
            DayOfWeek.Wednesday => "周三",
            DayOfWeek.Thursday => "周四",
            DayOfWeek.Friday => "周五",
            DayOfWeek.Saturday => "周六",
            DayOfWeek.Sunday => "周日",
            _ => "未知",
        };
}
