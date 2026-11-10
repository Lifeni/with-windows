using WithWindows.Core.Scheduling;

namespace WithWindows.Views;

public sealed class ScheduledTaskRunRowViewModel
{
    public ScheduledTaskRunRowViewModel(ScheduledTaskRun run)
    {
        Id = run.Id;
        TaskId = run.TaskId;
        StatusText = ScheduledTaskDisplayFormatter.RunStatusText(run.Status);
        StartedText = (run.StartedAt ?? run.ScheduledFor) is { } started
            ? started.ToLocalTime().ToString("M 月 d 日 HH:mm:ss")
            : "时间未知";
        DetailText = $"{StartedText}・{StatusText}";
        AutomationId = $"ScheduledTaskRunItem_{run.Id}";
        StdoutLogPath = run.StdoutLogPath;
        StderrLogPath = run.StderrLogPath;
        ErrorMessage = run.ErrorMessage;
    }

    public string Id { get; }
    public string TaskId { get; }
    public string StatusText { get; }
    public string StartedText { get; }
    public string DetailText { get; }
    public string AutomationId { get; }
    public string? StdoutLogPath { get; }
    public string? StderrLogPath { get; }
    public string? ErrorMessage { get; }

    public override string ToString() => DetailText;
}
