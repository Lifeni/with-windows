using WithWindows.Core.Scheduling;

namespace WithWindows.Views;

public sealed class ScheduledTaskRowViewModel
{
    public ScheduledTaskRowViewModel(
        ScheduledTask task,
        ScheduledTaskRun? latestRun,
        DateTimeOffset? nextRun)
    {
        Id = task.Id;
        Title = string.IsNullOrWhiteSpace(task.Name) ? "未命名任务" : task.Name;
        EnabledText = task.Enabled ? "已启用" : "已停用";
        ScheduleText = ScheduledTaskDisplayFormatter.ScheduleText(task);
        NextRunText = ScheduledTaskDisplayFormatter.IsExpired(
            task,
            DateTimeOffset.UtcNow)
                ? "已过期"
                : nextRun is { } next
                    ? $"下次：{next.ToLocalTime():M 月 d 日 HH:mm}"
                    : "下次：未安排";
        LastRunText = latestRun is null
            ? "尚未执行"
            : $"最近：{ScheduledTaskDisplayFormatter.RunStatusText(latestRun.Status)}";
        AutomationId = $"ScheduledTaskListItem_{task.Id}";
    }

    public string Id { get; }
    public string Title { get; }
    public string EnabledText { get; }
    public string ScheduleText { get; }
    public string NextRunText { get; }
    public string LastRunText { get; }
    public string AutomationId { get; }

    public override string ToString() => Title;
}
