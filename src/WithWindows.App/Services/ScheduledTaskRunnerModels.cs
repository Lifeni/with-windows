using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

public sealed record PythonRunRequest(
    string RunId,
    string PythonPath,
    string PythonVersion,
    string ScriptPath,
    string WorkingDirectory,
    int TimeoutSeconds,
    string StdoutLogPath,
    string StderrLogPath,
    ScriptSourceKind ScriptSource);

public sealed record PythonRunResult(
    ScheduledTaskRunStatus Status,
    int? ExitCode,
    string? ErrorMessage,
    bool StdoutTruncated,
    bool StderrTruncated);
