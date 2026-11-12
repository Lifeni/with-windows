using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

public sealed record CSharpRunRequest(
    string RunId,
    string ScriptPath,
    string WorkingDirectory,
    int TimeoutSeconds,
    string StdoutLogPath,
    string StderrLogPath);

public sealed record CSharpRunResult(
    ScheduledTaskRunStatus Status,
    int? ExitCode,
    string? ErrorMessage,
    bool StdoutTruncated,
    bool StderrTruncated);
