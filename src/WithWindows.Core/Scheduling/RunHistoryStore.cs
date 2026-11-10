using System.Text.Json;
using System.Text.Json.Serialization;
using WithWindows.Core.Diagnostics;

namespace WithWindows.Core.Scheduling;

public sealed class RunHistoryStore
{
    public const int DefaultLimitPerTask = 100;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private List<ScheduledTaskRun> _runs = [];

    public RunHistoryStore(string path)
    {
        Path = path;
        LoadFromDisk();
    }

    public string Path { get; }
    public string BackupPath => Path + ".bak";
    public StoreLoadState LoadState { get; private set; }
    public string? LastLoadError { get; private set; }

    public event EventHandler? Changed;

    public IReadOnlyList<ScheduledTaskRun> GetAll(string? taskId = null)
    {
        lock (_gate)
        {
            return _runs
                .Where(run => taskId is null || run.TaskId == taskId)
                .OrderByDescending(run => run.StartedAt ?? run.ScheduledFor ?? DateTimeOffset.MinValue)
                .ThenByDescending(run => run.Id, StringComparer.Ordinal)
                .Select(ScheduledTaskClone.Run)
                .ToList();
        }
    }

    public IReadOnlyList<string> Upsert(ScheduledTaskRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        if (!IsValidRun(run, Path))
            throw new ArgumentException("运行记录字段或日志路径无效。", nameof(run));

        List<string> prunedLogs = [];
        lock (_gate)
        {
            EnsureWritable();
            List<ScheduledTaskRun> next = _runs.Select(ScheduledTaskClone.Run).ToList();
            int index = next.FindIndex(item => item.Id == run.Id);
            if (index >= 0)
            {
                next[index] = ScheduledTaskClone.Run(run);
            }
            else
            {
                next.Add(ScheduledTaskClone.Run(run));
            }

            foreach (IGrouping<string, ScheduledTaskRun> group in next
                .GroupBy(item => item.TaskId, StringComparer.Ordinal))
            {
                List<ScheduledTaskRun> ordered = group
                    .OrderByDescending(item => item.StartedAt ?? item.ScheduledFor ?? DateTimeOffset.MinValue)
                    .ThenByDescending(item => item.Id, StringComparer.Ordinal)
                    .ToList();
                foreach (ScheduledTaskRun stale in ordered.Skip(DefaultLimitPerTask))
                {
                    next.Remove(stale);
                    if (!string.IsNullOrWhiteSpace(stale.StdoutLogPath))
                        prunedLogs.Add(stale.StdoutLogPath);
                    if (!string.IsNullOrWhiteSpace(stale.StderrLogPath))
                        prunedLogs.Add(stale.StderrLogPath);
                }
            }

            SaveDocument(next);
            _runs = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return prunedLogs;
    }

    public int RepairInterruptedRuns(DateTimeOffset now)
    {
        int repaired = 0;
        lock (_gate)
        {
            if (!CanWrite)
                return 0;

            List<ScheduledTaskRun> next = _runs.Select(ScheduledTaskClone.Run).ToList();
            foreach (ScheduledTaskRun run in next.Where(item =>
                item.Status == ScheduledTaskRunStatus.Running))
            {
                run.Status = ScheduledTaskRunStatus.Interrupted;
                run.FinishedAt = now;
                run.ErrorMessage = "应用上次未正常结束。";
                repaired++;
            }

            if (repaired > 0)
            {
                SaveDocument(next);
                _runs = next;
            }
        }

        if (repaired > 0)
            Changed?.Invoke(this, EventArgs.Empty);
        return repaired;
    }

    public IReadOnlyList<string> RemoveTask(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        var removedLogs = new List<string>();
        bool changed = false;
        lock (_gate)
        {
            EnsureWritable();
            List<ScheduledTaskRun> next = _runs
                .Where(run => run.TaskId != taskId)
                .Select(ScheduledTaskClone.Run)
                .ToList();
            foreach (ScheduledTaskRun removed in _runs.Where(run => run.TaskId == taskId))
            {
                if (!string.IsNullOrWhiteSpace(removed.StdoutLogPath))
                    removedLogs.Add(removed.StdoutLogPath);
                if (!string.IsNullOrWhiteSpace(removed.StderrLogPath))
                    removedLogs.Add(removed.StderrLogPath);
            }

            changed = next.Count != _runs.Count;
            if (changed)
            {
                SaveDocument(next);
                _runs = next;
            }
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
        return removedLogs;
    }

    public IReadOnlyList<string> RemoveTasksNotIn(IReadOnlySet<string> taskIds)
    {
        ArgumentNullException.ThrowIfNull(taskIds);
        var removedLogs = new List<string>();
        bool changed = false;
        lock (_gate)
        {
            if (!CanWrite)
                return [];

            List<ScheduledTaskRun> next = _runs
                .Where(run => taskIds.Contains(run.TaskId))
                .Select(ScheduledTaskClone.Run)
                .ToList();
            foreach (ScheduledTaskRun removed in _runs.Where(run =>
                !taskIds.Contains(run.TaskId)))
            {
                if (!string.IsNullOrWhiteSpace(removed.StdoutLogPath))
                    removedLogs.Add(removed.StdoutLogPath);
                if (!string.IsNullOrWhiteSpace(removed.StderrLogPath))
                    removedLogs.Add(removed.StderrLogPath);
            }

            changed = next.Count != _runs.Count;
            if (changed)
            {
                SaveDocument(next);
                _runs = next;
            }
        }

        if (changed)
            Changed?.Invoke(this, EventArgs.Empty);
        return removedLogs;
    }

    public bool CanWrite => LoadState != StoreLoadState.Faulted;

    private void LoadFromDisk()
    {
        bool mainExists = File.Exists(Path);
        bool backupExists = File.Exists(BackupPath);

        if (TryLoad(Path, out List<ScheduledTaskRun> loaded))
        {
            _runs = loaded;
            LoadState = StoreLoadState.Healthy;
            return;
        }

        if (TryLoad(BackupPath, out loaded))
        {
            try
            {
                if (mainExists)
                {
                    string corrupt = Path
                        + ".corrupt-"
                        + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
                    File.Move(Path, corrupt, overwrite: false);
                }

                WriteRecovered(loaded);
                _runs = loaded;
                LoadState = StoreLoadState.Recovered;
                LastLoadError = "执行历史损坏，已从备份恢复。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LoadState = StoreLoadState.Faulted;
                LastLoadError = $"执行历史恢复失败：{ex.Message}";
            }

            return;
        }

        if (!mainExists && !backupExists)
        {
            LoadState = StoreLoadState.Empty;
            return;
        }

        LoadState = StoreLoadState.Faulted;
        LastLoadError = "执行历史损坏，且没有可用备份。";
    }

    private static bool TryLoad(string path, out List<ScheduledTaskRun> runs)
    {
        runs = [];
        if (!File.Exists(path))
            return false;

        try
        {
            ScheduledTaskRunDocument? document =
                JsonSerializer.Deserialize<ScheduledTaskRunDocument>(
                    File.ReadAllText(path),
                    JsonOptions);
            if (document?.Runs is null
                || document.Version != ScheduledTaskRunDocument.CurrentVersion
                || document.Runs.Any(run => !IsValidRun(run, path)))
            {
                return false;
            }

            runs = document.Runs.Select(ScheduledTaskClone.Run).ToList();
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
            and not StackOverflowException)
        {
            return false;
        }
    }

    private static bool IsValidRun(ScheduledTaskRun? run, string historyPath)
    {
        if (run is null
            || !ScheduledTaskValidation.IsSafeRunId(run.Id)
            || !ScheduledTaskValidation.IsValidTaskId(run.TaskId)
            || run.TaskName is null
            || !Enum.IsDefined(run.Status)
            || !Enum.IsDefined(run.Trigger)
            || !Enum.IsDefined(run.ScriptSource))
        {
            return false;
        }

        string logsRoot = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(historyPath) ?? "",
            "logs");
        string taskLogsRoot = System.IO.Path.Combine(logsRoot, run.TaskId);
        return IsSafeLogPath(run.StdoutLogPath, taskLogsRoot)
            && IsSafeLogPath(run.StderrLogPath, taskLogsRoot);
    }

    private static bool IsSafeLogPath(string? path, string taskLogsRoot)
        => string.IsNullOrWhiteSpace(path)
            || (System.IO.Path.IsPathFullyQualified(path)
                && PathSafety.IsWithinDirectory(path, taskLogsRoot));

    private void WriteRecovered(List<ScheduledTaskRun> runs)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temp = Path + ".recover.tmp";
        try
        {
            File.WriteAllText(temp, Serialize(runs));
            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private void SaveDocument(List<ScheduledTaskRun> runs)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temp = Path + ".tmp";
        File.WriteAllText(temp, Serialize(runs));
        try
        {
            if (File.Exists(Path))
                File.Replace(temp, Path, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(temp, Path);
        }
        catch (IOException)
        {
            if (File.Exists(Path))
                File.Copy(Path, BackupPath, overwrite: true);
            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private static string Serialize(List<ScheduledTaskRun> runs)
        => JsonSerializer.Serialize(
            new ScheduledTaskRunDocument
            {
                Runs = runs.Select(ScheduledTaskClone.Run).ToList(),
            },
            JsonOptions);

    private void EnsureWritable()
    {
        if (!CanWrite)
            throw new InvalidOperationException(LastLoadError ?? "执行历史处于只读故障态。");
    }
}
