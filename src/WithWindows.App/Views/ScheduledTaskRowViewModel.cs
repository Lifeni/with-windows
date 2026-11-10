using Microsoft.UI.Xaml;
using WithWindows.Core.Scheduling;

namespace WithWindows.Views;

public sealed class ScheduledTaskRowViewModel
{
    public ScheduledTaskRowViewModel(
        ScheduledTask task,
        ScheduledTaskRun? latestRun,
        DateTimeOffset? nextRun,
        bool isRunning)
    {
        Id = task.Id;
        Title = string.IsNullOrWhiteSpace(task.Name) ? "未命名脚本" : task.Name;
        ScheduleText = ScheduledTaskDisplayFormatter.ScheduleText(task);
        TimingText = BuildTimingText(task, nextRun, isRunning);
        LastRunText = latestRun is null
            ? "尚未运行"
            : $"最近：{ScheduledTaskDisplayFormatter.RunStatusText(latestRun.Status)}";
        DetailText = $"{TimingText}・{LastRunText}";
        RunVisibility = isRunning ? Visibility.Collapsed : Visibility.Visible;
        StopVisibility = isRunning ? Visibility.Visible : Visibility.Collapsed;
        EditDeleteEnabled = !isRunning;
        AutomationId = $"ScheduledTaskListItem_{task.Id}";
        RunAutomationId = $"ScheduledTaskRun_{task.Id}";
        StopAutomationId = $"ScheduledTaskStop_{task.Id}";
        HistoryAutomationId = $"ScheduledTaskHistory_{task.Id}";
        EditAutomationId = $"ScheduledTaskEdit_{task.Id}";
        DeleteAutomationId = $"ScheduledTaskDelete_{task.Id}";
    }

    public string Id { get; }
    public string Title { get; }
    public string ScheduleText { get; }
    public string TimingText { get; }
    public string LastRunText { get; }
    public string DetailText { get; }
    public Visibility RunVisibility { get; }
    public Visibility StopVisibility { get; }
    public bool EditDeleteEnabled { get; }
    public string AutomationId { get; }
    public string RunAutomationId { get; }
    public string StopAutomationId { get; }
    public string HistoryAutomationId { get; }
    public string EditAutomationId { get; }
    public string DeleteAutomationId { get; }

    public override string ToString() => Title;

    private static string BuildTimingText(
        ScheduledTask task,
        DateTimeOffset? nextRun,
        bool isRunning)
    {
        if (isRunning)
            return "正在运行";
        if (task.Schedule.Kind == ScheduleKind.Manual)
            return "按需运行";
        if (task.Schedule.OneTimeConsumedAt is not null)
            return "单次计划已执行";
        if (ScheduledTaskDisplayFormatter.IsExpired(task, DateTimeOffset.UtcNow))
            return "单次计划已过期";
        return nextRun is { } next
            ? $"下次：{next.ToLocalTime():M 月 d 日 HH:mm}"
            : "单次计划未安排";
    }
}
