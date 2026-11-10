using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

/// <summary>
/// 脚本任务、单次认领和执行历史的单一写入协调器。
/// </summary>
public sealed class ScheduledTaskCoordinator : IDisposable
{
    private const string LuaRuntimeVersion = "5.4";

    private readonly AppHost _host;
    private readonly LuaScriptRunner _runner;
    private readonly object _gate = new();
    private readonly HashSet<string> _activeTasks = new(StringComparer.Ordinal);
    private readonly HashSet<string> _deletingTasks = new(StringComparer.Ordinal);
    private readonly HashSet<Task> _runTasks = [];
    private readonly Dictionary<string, CancellationTokenSource> _cancellations =
        new(StringComparer.Ordinal);
    private bool _stopping;
    private bool _disposed;

    public ScheduledTaskCoordinator(
        AppHost host,
        LuaScriptRunner runner)
    {
        _host = host;
        _runner = runner;
        Tasks = host.ScheduledTasks;
        Runs = host.ScheduledTaskRuns;
        Tasks.Changed += OnChanged;
        Runs.Changed += OnChanged;
        RepairClaimsWithoutRuns();
        Runs.RepairInterruptedRuns(DateTimeOffset.UtcNow);
        CleanupOrphanedData();
    }

    public ScheduledTaskStore Tasks { get; }
    public RunHistoryStore Runs { get; }
    public event EventHandler? Changed;

    public bool HasActiveTasks
    {
        get
        {
            lock (_gate)
                return _activeTasks.Count > 0;
        }
    }

    public bool IsTaskRunning(string taskId)
    {
        lock (_gate)
            return _activeTasks.Contains(taskId);
    }

    public ScheduledTask SaveTask(ScheduledTask task, DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (_gate)
        {
            EnsureAcceptingMutationsLocked();
            DateTimeOffset timestamp = now ?? DateTimeOffset.UtcNow;
            if (string.IsNullOrWhiteSpace(task.Id)
                || Tasks.Get(task.Id) is null)
            {
                task.CreatedAt = task.CreatedAt == default ? timestamp : task.CreatedAt;
                task.UpdatedAt = timestamp;
                return Tasks.Add(task);
            }

            return Tasks.Update(
                task.Id,
                current =>
                {
                    current.Name = task.Name;
                    current.Schedule = new TaskSchedule
                    {
                        Kind = task.Schedule.Kind,
                        OneTimeRunAt = task.Schedule.OneTimeRunAt,
                        OneTimeConsumedAt = task.Schedule.OneTimeConsumedAt,
                    };
                    current.ScriptText = task.ScriptText;
                    current.TimeoutSeconds = task.TimeoutSeconds;
                },
                timestamp);
        }
    }

    public Task DeleteTaskAsync(string taskId)
    {
        lock (_gate)
        {
            EnsureAcceptingMutationsLocked();
            if (!Tasks.CanWrite || !Runs.CanWrite)
            {
                throw new InvalidOperationException(
                    "脚本或执行历史处于只读故障态，不能删除脚本。");
            }

            if (_activeTasks.Contains(taskId))
                throw new InvalidOperationException("脚本正在运行，必须先停止。");
            if (!_deletingTasks.Add(taskId))
                throw new InvalidOperationException("脚本正在删除。");

            try
            {
                if (!Tasks.Delete(taskId))
                    return Task.CompletedTask;

                IReadOnlyList<string> removedLogs = Runs.RemoveTask(taskId);
                string taskLogDirectory = Path.Combine(
                    AppPaths.ScheduledTaskLogsDirectory,
                    taskId);
                DeleteScriptCopy(taskId, ".lua");
                DeleteScriptCopy(taskId, ".py");
                foreach (string path in removedLogs)
                    TryDeleteFile(path, taskLogDirectory);
                TryDeleteEmptyDirectory(
                    taskLogDirectory,
                    AppPaths.ScheduledTaskLogsDirectory);
            }
            finally
            {
                _deletingTasks.Remove(taskId);
            }
        }

        return Task.CompletedTask;
    }

    public bool BeginShutdown()
    {
        lock (_gate)
        {
            if (_disposed)
                return false;
            _stopping = true;
            return true;
        }
    }

    public Task RunNowAsync(string taskId, CancellationToken cancellationToken = default)
        => TrackRun(RunCoreAsync(
            taskId,
            occurrence: null,
            ScheduledTaskRunTrigger.Manual,
            cancellationToken));

    public Task RunScheduledAsync(
        ScheduledTaskOccurrence occurrence,
        ScheduledTaskRunTrigger trigger,
        CancellationToken cancellationToken = default)
        => TrackRun(RunCoreAsync(
            occurrence.TaskId,
            occurrence,
            trigger,
            cancellationToken));

    public bool StopTask(string taskId)
    {
        lock (_gate)
        {
            return _cancellations.TryGetValue(taskId, out CancellationTokenSource? source)
                && Cancel(source);
        }
    }

    public async Task StopAllAsync()
    {
        CancellationTokenSource[] sources;
        BeginShutdown();
        lock (_gate)
            sources = _cancellations.Values.ToArray();

        foreach (CancellationTokenSource source in sources)
            Cancel(source);

        await _runner.StopAllAsync();
        Task[] runs;
        lock (_gate)
            runs = _runTasks.ToArray();
        try
        {
            await Task.WhenAll(runs).WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException ex)
        {
            _host.Log.Error($"等待 Lua 脚本收尾超时：{ex.Message}");
        }
        catch (Exception ex)
        {
            _host.Log.Error($"等待 Lua 脚本收尾失败：{ex.Message}");
        }
    }

    private async Task RunCoreAsync(
        string taskId,
        ScheduledTaskOccurrence? occurrence,
        ScheduledTaskRunTrigger trigger,
        CancellationToken cancellationToken)
    {
        if (!Runs.CanWrite)
        {
            _host.Log.Error(
                "执行历史处于只读故障态，拒绝启动脚本并保留待执行时间。");
            return;
        }

        ScheduledTask? task = Tasks.Get(taskId);
        if (task is null)
            return;

        string runId = CreateRunId();
        bool overlap;
        CancellationTokenSource? runCancellation = null;
        lock (_gate)
        {
            if (_stopping
                || _disposed
                || _deletingTasks.Contains(taskId))
            {
                return;
            }

            overlap = !_activeTasks.Add(taskId);
            if (!overlap)
            {
                runCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
                _cancellations[taskId] = runCancellation;
            }
        }

        if (overlap)
        {
            RecordSkipped(task, occurrence, trigger, runId);
            return;
        }

        try
        {
            if (occurrence is not null)
            {
                task = Tasks.ClaimOccurrence(
                    taskId,
                    occurrence,
                    runId,
                    DateTimeOffset.UtcNow);
            }

            string scriptPath = ScriptCopyPath(taskId);
            string stdoutPath = Path.Combine(
                AppPaths.ScheduledTaskLogsDirectory,
                taskId,
                runId + ".stdout.log");
            string stderrPath = Path.Combine(
                AppPaths.ScheduledTaskLogsDirectory,
                taskId,
                runId + ".stderr.log");
            ScheduledTaskRun running = CreateRun(
                task,
                runId,
                occurrence,
                trigger,
                scriptPath,
                stdoutPath,
                stderrPath,
                ScheduledTaskRunStatus.Running);
            Runs.Upsert(running);
            _host.Log.Info(
                $"Lua 脚本“{task.Name}”开始运行：{runId}，触发={trigger}");

            MaterializeScript(task, runId);
            string? validationError = ValidateExecution(scriptPath);
            LuaRunResult result;
            if (validationError is not null)
            {
                result = new LuaRunResult(
                    ScheduledTaskRunStatus.Failed,
                    ExitCode: null,
                    validationError,
                    StdoutTruncated: false,
                    StderrTruncated: false);
            }
            else
            {
                result = await _runner.RunAsync(
                    new LuaRunRequest(
                        runId,
                        scriptPath,
                        ResolveWorkingDirectory(),
                        task.TimeoutSeconds,
                        stdoutPath,
                        stderrPath),
                    runCancellation!.Token);
            }

            running.Status = result.Status;
            running.ExitCode = result.ExitCode;
            running.FinishedAt = DateTimeOffset.UtcNow;
            running.ErrorMessage = BuildOutputMessage(
                result.ErrorMessage,
                result.StdoutTruncated,
                result.StderrTruncated);
            IReadOnlyList<string> prunedLogs = Runs.Upsert(running);
            _host.Log.Info(
                $"Lua 脚本“{task.Name}”运行结束：{runId}，"
                + $"状态={result.Status}，退出码={result.ExitCode?.ToString() ?? "无"}");
            foreach (string path in prunedLogs)
                TryDeleteFile(path, AppPaths.ScheduledTaskLogsDirectory);
        }
        catch (Exception ex)
        {
            _host.Log.Error($"Lua 脚本“{task.Name}”执行失败：{ex}");
            TryRecordFailure(
                task,
                runId,
                occurrence,
                trigger,
                ex.Message);
        }
        finally
        {
            if (runCancellation is not null)
                runCancellation.Dispose();
            lock (_gate)
            {
                _activeTasks.Remove(taskId);
                _cancellations.Remove(taskId);
            }
        }
    }

    private void RecordSkipped(
        ScheduledTask task,
        ScheduledTaskOccurrence? occurrence,
        ScheduledTaskRunTrigger trigger,
        string runId)
    {
        try
        {
            if (occurrence is not null)
            {
                task = Tasks.ClaimOccurrence(
                    task.Id,
                    occurrence,
                    runId,
                    DateTimeOffset.UtcNow);
            }

            ScheduledTaskRun run = CreateRun(
                task,
                runId,
                occurrence,
                trigger,
                ScriptCopyPath(task.Id),
                StdoutLogPath: null,
                StderrLogPath: null,
                ScheduledTaskRunStatus.Skipped);
            run.FinishedAt = run.StartedAt;
            run.ErrorMessage = "上一次运行尚未结束。";
            Runs.Upsert(run);
        }
        catch (Exception ex)
        {
            _host.Log.Error($"记录 Lua 脚本跳过状态失败：{ex.Message}");
        }
    }

    private void TryRecordFailure(
        ScheduledTask task,
        string runId,
        ScheduledTaskOccurrence? occurrence,
        ScheduledTaskRunTrigger trigger,
        string message)
    {
        try
        {
            ScheduledTaskRun? existing = Runs
                .GetAll(task.Id)
                .FirstOrDefault(run => run.Id == runId);
            if (existing is not null)
            {
                existing.Status = ScheduledTaskRunStatus.Failed;
                existing.FinishedAt = DateTimeOffset.UtcNow;
                existing.ErrorMessage = message;
                Runs.Upsert(existing);
                return;
            }

            ScheduledTaskRun failed = CreateRun(
                task,
                runId,
                occurrence,
                trigger,
                ScriptCopyPath(task.Id),
                StdoutLogPath: null,
                StderrLogPath: null,
                ScheduledTaskRunStatus.Failed);
            failed.FinishedAt = DateTimeOffset.UtcNow;
            failed.ErrorMessage = message;
            Runs.Upsert(failed);
        }
        catch (Exception ex)
        {
            _host.Log.Error($"记录 Lua 脚本失败状态失败：{ex.Message}");
        }
    }

    private static ScheduledTaskRun CreateRun(
        ScheduledTask task,
        string runId,
        ScheduledTaskOccurrence? occurrence,
        ScheduledTaskRunTrigger trigger,
        string scriptPath,
        string? StdoutLogPath,
        string? StderrLogPath,
        ScheduledTaskRunStatus status)
        => new()
        {
            Id = runId,
            TaskId = task.Id,
            TaskName = task.Name,
            Trigger = trigger,
            ScheduledFor = occurrence?.ScheduledFor,
            StartedAt = DateTimeOffset.UtcNow,
            Status = status,
            RuntimeVersion = LuaRuntimeVersion,
            ScriptPath = scriptPath,
            StdoutLogPath = StdoutLogPath,
            StderrLogPath = StderrLogPath,
        };

    private static string ScriptCopyPath(string taskId)
        => Path.Combine(
            AppPaths.ScheduledTaskScriptsDirectory,
            taskId + ".lua");

    private static void MaterializeScript(ScheduledTask task, string runId)
    {
        if (string.IsNullOrWhiteSpace(task.ScriptText))
            throw new InvalidOperationException("Lua 脚本为空。");

        Directory.CreateDirectory(AppPaths.ScheduledTaskScriptsDirectory);
        string path = ScriptCopyPath(task.Id);
        if (!PathSafety.IsWithinDirectory(
                path,
                AppPaths.ScheduledTaskScriptsDirectory))
        {
            throw new InvalidOperationException("脚本运行副本路径越界。");
        }

        string temp = path + "." + runId + ".tmp";
        File.WriteAllText(temp, task.ScriptText, new UTF8Encoding(false));
        File.Move(temp, path, overwrite: true);
    }

    private static string? ValidateExecution(string scriptPath)
        => File.Exists(scriptPath)
            ? null
            : $"Lua 脚本运行副本不存在：{scriptPath}";

    private static string ResolveWorkingDirectory()
        => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static string? BuildOutputMessage(
        string? original,
        bool stdoutTruncated,
        bool stderrTruncated)
    {
        if (!stdoutTruncated && !stderrTruncated)
            return original;

        string truncated = "输出超过上限，已截断。";
        return string.IsNullOrWhiteSpace(original)
            ? truncated
            : original + " " + truncated;
    }

    private void RepairClaimsWithoutRuns()
    {
        HashSet<string> runIds = Runs.GetAll()
            .Select(run => run.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (ScheduledTask task in Tasks.GetAll())
        {
            if (task.LastHandledRunId is not { Length: > 0 } runId
                || runIds.Contains(runId))
            {
                continue;
            }

            try
            {
                ScheduledTaskRun interrupted = CreateRun(
                    task,
                    runId,
                    occurrence: null,
                    ScheduledTaskRunTrigger.Scheduled,
                    ScriptCopyPath(task.Id),
                    StdoutLogPath: null,
                    StderrLogPath: null,
                    ScheduledTaskRunStatus.Interrupted);
                interrupted.ScheduledFor = task.LastHandledScheduledFor;
                interrupted.StartedAt = task.LastHandledScheduledFor;
                interrupted.FinishedAt = DateTimeOffset.UtcNow;
                interrupted.ErrorMessage = "认领后未完成启动。";
                Runs.Upsert(interrupted);
            }
            catch (Exception ex)
            {
                _host.Log.Error($"修复脚本认领记录失败：{ex.Message}");
            }
        }
    }

    private void CleanupOrphanedData()
    {
        if (Tasks.LoadState == StoreLoadState.Faulted
            || Runs.LoadState == StoreLoadState.Faulted)
        {
            return;
        }

        HashSet<string> taskIds = Tasks.GetAll()
            .Select(task => task.Id)
            .ToHashSet(StringComparer.Ordinal);

        foreach (string path in Runs.RemoveTasksNotIn(taskIds))
            TryDeleteFile(path, AppPaths.ScheduledTaskLogsDirectory);

        if (Directory.Exists(AppPaths.ScheduledTaskScriptsDirectory))
        {
            foreach (string path in Directory.EnumerateFiles(
                AppPaths.ScheduledTaskScriptsDirectory,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                string extension = Path.GetExtension(path);
                if (extension is not (".lua" or ".py"))
                    continue;

                string id = Path.GetFileNameWithoutExtension(path);
                if (!ScheduledTaskValidation.IsValidTaskId(id)
                    || !taskIds.Contains(id)
                    || extension == ".py")
                {
                    TryDeleteFile(path, AppPaths.ScheduledTaskScriptsDirectory);
                }
            }
        }

        if (Directory.Exists(AppPaths.ScheduledTaskLogsDirectory))
        {
            HashSet<string> referencedLogs = Runs.GetAll()
                .SelectMany(run => new[] { run.StdoutLogPath, run.StderrLogPath })
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => Path.GetFullPath(path!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (string directory in Directory.EnumerateDirectories(
                AppPaths.ScheduledTaskLogsDirectory,
                "*",
                SearchOption.TopDirectoryOnly))
            {
                string id = Path.GetFileName(directory);
                if (!ScheduledTaskValidation.IsValidTaskId(id)
                    || !taskIds.Contains(id))
                {
                    TryDeleteDirectoryTree(
                        directory,
                        AppPaths.ScheduledTaskLogsDirectory);
                    continue;
                }

                foreach (string file in Directory.EnumerateFiles(directory))
                {
                    if (!referencedLogs.Contains(Path.GetFullPath(file)))
                        TryDeleteFile(file, directory);
                }

                TryDeleteEmptyDirectory(
                    directory,
                    AppPaths.ScheduledTaskLogsDirectory);
            }
        }

        if (Directory.Exists(AppPaths.ScheduledTaskRunnerDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(
                AppPaths.ScheduledTaskRunnerDirectory,
                "*.json",
                SearchOption.TopDirectoryOnly))
            {
                TryDeleteFile(file, AppPaths.ScheduledTaskRunnerDirectory);
            }
        }

        Directory.CreateDirectory(AppPaths.ScheduledTaskScriptsDirectory);
        Directory.CreateDirectory(AppPaths.ScheduledTaskLogsDirectory);
        Directory.CreateDirectory(AppPaths.ScheduledTaskRunnerDirectory);
    }

    private static string CreateRunId()
        => DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff")
            + "_"
            + Guid.NewGuid().ToString("N")[..8];

    private static bool Cancel(CancellationTokenSource source)
    {
        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private static void DeleteScriptCopy(string taskId, string extension)
    {
        string path = Path.Combine(
            AppPaths.ScheduledTaskScriptsDirectory,
            taskId + extension);
        TryDeleteFile(path, AppPaths.ScheduledTaskScriptsDirectory);
    }

    private static void TryDeleteFile(string? path, string allowedDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !PathSafety.IsWithinDirectory(path, allowedDirectory))
            return;

        try
        {
            File.Delete(path);
        }
        catch
        {
            // 清理失败由启动垃圾回收或下一次历史裁剪处理。
        }
    }

    private void EnsureAcceptingMutationsLocked()
    {
        if (_stopping || _disposed)
            throw new InvalidOperationException("应用正在退出，不能修改脚本任务。");
    }

    private static void TryDeleteEmptyDirectory(
        string? path,
        string allowedDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !PathSafety.IsWithinDirectory(path, allowedDirectory))
            return;

        try
        {
            if (Directory.Exists(path)
                && !Directory.EnumerateFileSystemEntries(path).Any())
            {
                Directory.Delete(path);
            }
        }
        catch
        {
            // 目录清理失败不影响任务删除结果。
        }
    }

    private static void TryDeleteDirectoryTree(
        string? path,
        string allowedDirectory)
    {
        if (string.IsNullOrWhiteSpace(path)
            || !PathSafety.IsWithinDirectory(path, allowedDirectory))
            return;

        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch
        {
            // 目录正被占用时由下一次启动继续清理。
        }
    }

    private void OnChanged(object? sender, EventArgs args)
        => Changed?.Invoke(this, EventArgs.Empty);

    private Task TrackRun(Task task)
    {
        lock (_gate)
            _runTasks.Add(task);

        _ = task.ContinueWith(
            completed =>
            {
                if (completed.IsFaulted)
                    _host.Log.Error($"Lua 脚本任务异常结束：{completed.Exception}");
                lock (_gate)
                    _runTasks.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _stopping = true;
        Tasks.Changed -= OnChanged;
        Runs.Changed -= OnChanged;
    }
}
