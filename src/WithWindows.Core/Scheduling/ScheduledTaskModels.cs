namespace WithWindows.Core.Scheduling;

public enum ScheduleKind
{
    Once,
    Daily,
    Weekly,
}

public enum ScriptSourceKind
{
    Inline,
    File,
}

public enum PythonInterpreterSource
{
    Path,
    PyLauncher,
    StoreAlias,
    Manual,
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
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<ScheduledTask> Tasks { get; set; } = [];
}

public sealed class ScheduledTask
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public bool Enabled { get; set; }
    public int ScheduleRevision { get; set; } = 1;
    public DateTimeOffset? LastHandledScheduledFor { get; set; }
    public string? LastHandledRunId { get; set; }
    public TaskSchedule Schedule { get; set; } = new();
    public ScriptSource Source { get; set; } = new();
    public string? PythonPath { get; set; }
    public PythonInterpreterSource PythonSource { get; set; }
    public string? PythonResolvedPath { get; set; }
    public string? PythonPackageFamilyName { get; set; }
    public string? WorkingDirectory { get; set; }
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
    public DateTimeOffset? OneTimeConsumedAt { get; set; }
}

public sealed class ScriptSource
{
    public ScriptSourceKind Kind { get; set; }
    public string? ScriptText { get; set; }
    public string? ScriptPath { get; set; }
}

public sealed record ScheduledTaskOccurrence(
    string TaskId,
    int ScheduleRevision,
    DateTimeOffset ScheduledFor);

public sealed class ScheduledTaskRunDocument
{
    public const int CurrentVersion = 1;

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
    public string? PythonPath { get; set; }
    public string? PythonVersion { get; set; }
    public ScriptSourceKind ScriptSource { get; set; }
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
        Enabled = task.Enabled,
        ScheduleRevision = task.ScheduleRevision,
        LastHandledScheduledFor = task.LastHandledScheduledFor,
        LastHandledRunId = task.LastHandledRunId,
        Schedule = Schedule(task.Schedule),
        Source = Source(task.Source),
        PythonPath = task.PythonPath,
        PythonSource = task.PythonSource,
        PythonResolvedPath = task.PythonResolvedPath,
        PythonPackageFamilyName = task.PythonPackageFamilyName,
        WorkingDirectory = task.WorkingDirectory,
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
        OneTimeConsumedAt = schedule.OneTimeConsumedAt,
    };

    public static ScriptSource Source(ScriptSource source) => new()
    {
        Kind = source.Kind,
        ScriptText = source.ScriptText,
        ScriptPath = source.ScriptPath,
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
        PythonPath = run.PythonPath,
        PythonVersion = run.PythonVersion,
        ScriptSource = run.ScriptSource,
        ScriptPath = run.ScriptPath,
        StdoutLogPath = run.StdoutLogPath,
        StderrLogPath = run.StderrLogPath,
        ErrorMessage = run.ErrorMessage,
    };
}
