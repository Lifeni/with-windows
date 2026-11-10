namespace WithWindows.Core.Scheduling;

public enum ScheduleKind
{
    Manual,
    Once,
    Daily,
    Weekly,
    Monthly,
}

public enum ScheduledTaskRunStatus
{
    Running,
    Succeeded,
    Failed,
    TimedOut,
    Cancelled,
    Skipped,
    Interrupted,
}

public enum ScheduledTaskRunTrigger
{
    Scheduled,
    Late,
    Manual,
}

public enum StoreLoadState
{
    Empty,
    Healthy,
    Recovered,
    Faulted,
}

public sealed class ScheduledTaskDocument
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<ScheduledTask> Tasks { get; set; } = [];
}

public sealed class ScheduledTask
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int ScheduleRevision { get; set; } = 1;
    public DateTimeOffset? LastHandledScheduledFor { get; set; }
    public string? LastHandledRunId { get; set; }
    public TaskSchedule Schedule { get; set; } = new();
    public string ScriptText { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 600;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public sealed class TaskSchedule
{
    public ScheduleKind Kind { get; set; }
    public DateTimeOffset? OneTimeRunAt { get; set; }
    public TimeOnly? TimeOfDay { get; set; }
    public List<DayOfWeek> DaysOfWeek { get; set; } = [];
    public int? DayOfMonth { get; set; }
    public DateTimeOffset? OneTimeConsumedAt { get; set; }
}

public sealed record ScheduledTaskOccurrence(
    string TaskId,
    int ScheduleRevision,
    DateTimeOffset ScheduledFor);

public sealed class ScheduledTaskRunDocument
{
    public const int CurrentVersion = 2;

    public int Version { get; set; } = CurrentVersion;
    public List<ScheduledTaskRun> Runs { get; set; } = [];
}

public sealed class ScheduledTaskRun
{
    public string Id { get; set; } = "";
    public string TaskId { get; set; } = "";
    public string TaskName { get; set; } = "";
    public ScheduledTaskRunTrigger Trigger { get; set; }
    public DateTimeOffset? ScheduledFor { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public ScheduledTaskRunStatus Status { get; set; }
    public int? ExitCode { get; set; }
    public string? RuntimeVersion { get; set; }
    public string? ScriptPath { get; set; }
    public string? StdoutLogPath { get; set; }
    public string? StderrLogPath { get; set; }
    public string? ErrorMessage { get; set; }
}

internal static class ScheduledTaskClone
{
    public static ScheduledTask Task(ScheduledTask task) => new()
    {
        Id = task.Id,
        Name = task.Name,
        ScheduleRevision = task.ScheduleRevision,
        LastHandledScheduledFor = task.LastHandledScheduledFor,
        LastHandledRunId = task.LastHandledRunId,
        Schedule = Schedule(task.Schedule),
        ScriptText = task.ScriptText,
        TimeoutSeconds = task.TimeoutSeconds,
        CreatedAt = task.CreatedAt,
        UpdatedAt = task.UpdatedAt,
    };

    public static TaskSchedule Schedule(TaskSchedule schedule) => new()
    {
        Kind = schedule.Kind,
        OneTimeRunAt = schedule.OneTimeRunAt,
        TimeOfDay = schedule.TimeOfDay,
        DaysOfWeek = [.. schedule.DaysOfWeek],
        DayOfMonth = schedule.DayOfMonth,
        OneTimeConsumedAt = schedule.OneTimeConsumedAt,
    };

    public static ScheduledTaskRun Run(ScheduledTaskRun run) => new()
    {
        Id = run.Id,
        TaskId = run.TaskId,
        TaskName = run.TaskName,
        Trigger = run.Trigger,
        ScheduledFor = run.ScheduledFor,
        StartedAt = run.StartedAt,
        FinishedAt = run.FinishedAt,
        Status = run.Status,
        ExitCode = run.ExitCode,
        RuntimeVersion = run.RuntimeVersion,
        ScriptPath = run.ScriptPath,
        StdoutLogPath = run.StdoutLogPath,
        StderrLogPath = run.StderrLogPath,
        ErrorMessage = run.ErrorMessage,
    };
}
