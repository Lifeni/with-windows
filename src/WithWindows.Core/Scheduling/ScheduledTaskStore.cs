using System.Text.Json;
using System.Text.Json.Serialization;

namespace WithWindows.Core.Scheduling;

public sealed class ScheduledTaskStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private List<ScheduledTask> _tasks = [];

    public ScheduledTaskStore(string path)
    {
        Path = path;
        LoadFromDisk();
    }

    public string Path { get; }
    public string BackupPath => Path + ".bak";
    public StoreLoadState LoadState { get; private set; }
    public string? LastLoadError { get; private set; }
    public bool CanWrite => LoadState != StoreLoadState.Faulted;

    public event EventHandler? Changed;

    public IReadOnlyList<ScheduledTask> GetAll()
    {
        lock (_gate)
            return _tasks.Select(ScheduledTaskClone.Task).ToList();
    }

    public ScheduledTask? Get(string id)
    {
        lock (_gate)
        {
            ScheduledTask? task = _tasks.FirstOrDefault(item => item.Id == id);
            return task is null ? null : ScheduledTaskClone.Task(task);
        }
    }

    public ScheduledTask Add(ScheduledTask task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ScheduledTask normalized = ScheduledTaskClone.Task(task);
        normalized.Id = string.IsNullOrWhiteSpace(normalized.Id)
            ? Guid.NewGuid().ToString("N")
            : normalized.Id;
        if (!ScheduledTaskValidation.IsValidTaskId(normalized.Id))
            throw new ArgumentException("任务 ID 必须是 32 位 GUID。", nameof(task));
        ScheduledTaskValidation.ValidateForSave(normalized);
        if (normalized.CreatedAt == default)
            normalized.CreatedAt = DateTimeOffset.UtcNow;
        normalized.UpdatedAt = normalized.CreatedAt;
        _ = ScheduledTaskValidation.Normalize(normalized);

        Mutate(tasks =>
        {
            if (tasks.Any(item => item.Id == normalized.Id))
                throw new InvalidOperationException("任务 ID 已存在。");
            tasks.Add(ScheduledTaskClone.Task(normalized));
        });
        return ScheduledTaskClone.Task(normalized);
    }

    public ScheduledTask Update(
        string id,
        Action<ScheduledTask> mutation,
        DateTimeOffset now)
        => UpdateCore(id, mutation, now, planChangedOnly: false);

    public ScheduledTask UpdatePlan(
        string id,
        Action<TaskSchedule> mutation,
        DateTimeOffset now)
        => UpdateCore(
            id,
            task => mutation(task.Schedule),
            now,
            planChangedOnly: false);

    public ScheduledTask ClaimOccurrence(
        string id,
        ScheduledTaskOccurrence occurrence,
        string runId,
        DateTimeOffset now)
    {
        if (occurrence.TaskId != id)
            throw new ArgumentException("occurrence 与任务不匹配。", nameof(occurrence));

        return UpdateCore(
            id,
            task =>
            {
                if (task.ScheduleRevision != occurrence.ScheduleRevision)
                    throw new InvalidOperationException("计划修订号已变化。");

                if (task.LastHandledScheduledFor is { } handled
                    && occurrence.ScheduledFor <= handled)
                {
                    throw new InvalidOperationException("该 occurrence 已经被处理。");
                }

                ScheduledTaskValidation.ValidateForSave(task);
                task.LastHandledScheduledFor = occurrence.ScheduledFor;
                task.LastHandledRunId = runId;
                if (task.Schedule.Kind == ScheduleKind.Once)
                    task.Schedule.OneTimeConsumedAt = now;
            },
            now,
            planChangedOnly: true,
            allowSamePlan: true);
    }

    public bool Delete(string id)
    {
        bool changed = false;
        Mutate(tasks => changed = tasks.RemoveAll(item => item.Id == id) > 0);
        return changed;
    }

    private ScheduledTask UpdateCore(
        string id,
        Action<ScheduledTask> mutation,
        DateTimeOffset now,
        bool planChangedOnly,
        bool allowSamePlan = false)
    {
        ScheduledTask result = null!;
        Mutate(tasks =>
        {
            ScheduledTask? current = tasks.FirstOrDefault(item => item.Id == id);
            if (current is null)
                throw new InvalidOperationException("任务不存在。");

            TaskSchedule before = ScheduledTaskClone.Schedule(current.Schedule);
            ScheduledTask next = ScheduledTaskClone.Task(current);
            mutation(next);
            _ = ScheduledTaskValidation.Normalize(next);
            ScheduledTaskValidation.ValidateForSave(next);

            bool planChanged = !ScheduledTaskValidation.ScheduleEquals(before, next.Schedule);
            if (planChanged && !allowSamePlan)
            {
                next.ScheduleRevision = Math.Max(1, next.ScheduleRevision + 1);
                next.LastHandledScheduledFor = null;
                next.LastHandledRunId = null;
                next.Schedule.OneTimeConsumedAt = null;
            }

            if (planChangedOnly && !planChanged && !allowSamePlan)
                throw new InvalidOperationException("计划没有变化。");

            next.UpdatedAt = now;
            int index = tasks.IndexOf(current);
            tasks[index] = next;
            result = ScheduledTaskClone.Task(next);
        });
        return result;
    }

    private void Mutate(Action<List<ScheduledTask>> mutation)
    {
        lock (_gate)
        {
            EnsureWritable();
            List<ScheduledTask> next = _tasks
                .Select(ScheduledTaskClone.Task)
                .ToList();
            mutation(next);
            SaveDocument(next);
            _tasks = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void LoadFromDisk()
    {
        bool mainExists = File.Exists(Path);
        bool backupExists = File.Exists(BackupPath);

        if (TryLoad(Path, out List<ScheduledTask> loaded))
        {
            _tasks = loaded;
            LoadState = StoreLoadState.Healthy;
            return;
        }

        if (TryLoad(BackupPath, out List<ScheduledTask> backup))
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

                WriteRecovered(backup);
                _tasks = backup;
                LoadState = StoreLoadState.Recovered;
                LastLoadError = "任务文件损坏，已从备份恢复。";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LoadState = StoreLoadState.Faulted;
                LastLoadError = $"任务文件恢复失败：{ex.Message}";
            }

            return;
        }

        if (!mainExists && !backupExists)
        {
            _tasks = [];
            LoadState = StoreLoadState.Empty;
            return;
        }

        _tasks = [];
        LoadState = StoreLoadState.Faulted;
        LastLoadError = "任务文件损坏，且没有可用备份。";
    }

    private static bool TryLoad(string path, out List<ScheduledTask> tasks)
    {
        tasks = [];
        if (!File.Exists(path))
            return false;

        try
        {
            ScheduledTaskDocument? document = JsonSerializer.Deserialize<ScheduledTaskDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (document?.Tasks is null
                || document.Version is not (1 or ScheduledTaskDocument.CurrentVersion)
                || (document.Version == 1 && document.Tasks.Count > 0)
                || document.Tasks.Any(task => task is null
                    || !ScheduledTaskValidation.IsValidTaskId(task.Id)
                    || task.Name is null
                    || task.Schedule is null
                    || task.ScriptText is null))
            {
                return false;
            }

            tasks = document.Tasks
                .Select(ScheduledTaskClone.Task)
                .ToList();
            foreach (ScheduledTask task in tasks)
            {
                _ = ScheduledTaskValidation.Normalize(task);
                ScheduledTaskValidation.ValidateForSave(task);
            }
            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException
            and not StackOverflowException)
        {
            return false;
        }
    }

    private void WriteRecovered(List<ScheduledTask> tasks)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string json = JsonSerializer.Serialize(
            new ScheduledTaskDocument
            {
                Tasks = tasks.Select(ScheduledTaskClone.Task).ToList(),
            },
            JsonOptions);
        string temp = Path + ".recover.tmp";
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    private void SaveDocument(List<ScheduledTask> tasks)
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temp = Path + ".tmp";
        string json = JsonSerializer.Serialize(
            new ScheduledTaskDocument
            {
                Tasks = tasks.Select(ScheduledTaskClone.Task).ToList(),
            },
            JsonOptions);
        File.WriteAllText(temp, json);

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

    private void EnsureWritable()
    {
        if (!CanWrite)
            throw new InvalidOperationException(LastLoadError ?? "任务文件处于只读故障态。");
    }
}
