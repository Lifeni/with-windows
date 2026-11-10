using WithWindows.Core.Scheduling;

namespace WithWindows.Views;

public sealed class ScheduledTaskRunRowViewModel
{
    public ScheduledTaskRunRowViewModel(ScheduledTaskRun run)
    {
        Id = run.Id;
        TaskId = run.TaskId;
        StatusText = ScheduledTaskDisplayFormatter.RunStatusText(run.Status);
        TriggerText = run.Trigger switch
        {
            ScheduledTaskRunTrigger.Manual => "手动运行",
            ScheduledTaskRunTrigger.Scheduled => "按计划运行",
            ScheduledTaskRunTrigger.Late => "延迟补跑",
            _ => "未知触发方式",
        };
        StartedText = (run.StartedAt ?? run.ScheduledFor) is { } started
            ? started.ToLocalTime().ToString("M 月 d 日 HH:mm:ss")
            : "时间未知";
        ListText = (run.StartedAt ?? run.ScheduledFor) is { } listStarted
            ? $"{listStarted.ToLocalTime():M/d HH:mm:ss}・{StatusText}"
            : $"时间未知・{StatusText}";
        DurationText = BuildDurationText(run);
        ExitCodeText = run.ExitCode is { } exitCode
            ? $"退出码 {exitCode}"
            : "无退出码";
        DetailText = $"{StartedText}・{StatusText}";
        MetaText = $"{StatusText}・{TriggerText}・{DurationText}・{ExitCodeText}";
        AutomationId = $"ScheduledTaskRunItem_{run.Id}";
        StdoutLogPath = run.StdoutLogPath;
        StderrLogPath = run.StderrLogPath;
        ErrorMessage = run.ErrorMessage;
    }

    public string Id { get; }
    public string TaskId { get; }
    public string StatusText { get; }
    public string TriggerText { get; }
    public string StartedText { get; }
    public string ListText { get; }
    public string DurationText { get; }
    public string ExitCodeText { get; }
    public string DetailText { get; }
    public string MetaText { get; }
    public string AutomationId { get; }
    public string? StdoutLogPath { get; }
    public string? StderrLogPath { get; }
    public string? ErrorMessage { get; }

    public override string ToString() => ListText;

    private static string BuildDurationText(ScheduledTaskRun run)
    {
        if (run.StartedAt is not { } started
            || run.FinishedAt is not { } finished
            || finished < started)
        {
            return "耗时未知";
        }

        double seconds = (finished - started).TotalSeconds;
        if (seconds < 1)
            return "不到 1 秒";
        if (seconds < 60)
            return $"{Math.Ceiling(seconds):0} 秒";

        int wholeSeconds = (int)Math.Round(seconds);
        return wholeSeconds < 3600
            ? $"{wholeSeconds / 60} 分 {wholeSeconds % 60} 秒"
            : $"{wholeSeconds / 3600} 小时 {wholeSeconds % 3600 / 60} 分";
    }
}
