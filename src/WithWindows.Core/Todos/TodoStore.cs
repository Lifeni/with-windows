using System.Text.Json;

namespace WithWindows.Core.Todos;

/// <summary>待办仓储；页面与提醒调度共享同一实例，所有修改都会原子写入 JSON。</summary>
public sealed class TodoStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly object _gate = new();
    private List<TodoItem> _items = [];

    public TodoStore(string path)
    {
        Path = path;
        LoadFromDisk();
    }

    public string Path { get; }

    /// <summary>上一次保存成功的待办数据；主文件损坏时用于恢复。</summary>
    public string BackupPath => Path + ".bak";

    /// <summary>启动时主文件与备份都无法读取时的错误说明。</summary>
    public string? LastLoadError { get; private set; }

    /// <summary>待办发生变化后触发；事件在写盘完成后、锁之外触发。</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<TodoItem> GetAll()
    {
        lock (_gate)
            return _items.Select(Clone).ToList();
    }

    public TodoItem Add(string title, DateTimeOffset now)
    {
        string normalized = title.Trim();
        if (normalized.Length == 0)
            throw new ArgumentException("待办标题不能为空。", nameof(title));

        var item = new TodoItem
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = normalized,
            CreatedAt = now,
        };

        Mutate(items =>
        {
            items.Add(item);
            return true;
        });
        return Clone(item);
    }

    public bool SetCompleted(string id, bool completed, DateTimeOffset now)
        => Mutate(items =>
        {
            TodoItem? item = items.FirstOrDefault(candidate => candidate.Id == id);
            if (item is null || item.IsCompleted == completed)
                return false;

            item.IsCompleted = completed;
            item.CompletedAt = completed ? now : null;
            if (completed)
            {
                item.ReminderAt = null;
                item.ReminderDeliveredAt = null;
            }

            return true;
        });

    public bool SetReminder(
        string id,
        DateTimeOffset? reminderAt,
        DateTimeOffset now)
        => Mutate(items =>
        {
            TodoItem? item = items.FirstOrDefault(candidate => candidate.Id == id);
            if (item is null || item.IsCompleted)
                return false;

            if (item.ReminderAt == reminderAt && reminderAt is not null)
                return false;

            item.ReminderAt = reminderAt;
            item.ReminderDeliveredAt = null;
            return true;
        });

    public bool MarkReminderDelivered(string id, DateTimeOffset deliveredAt)
        => Mutate(items =>
        {
            TodoItem? item = items.FirstOrDefault(candidate => candidate.Id == id);
            if (item is null
                || item.IsCompleted
                || item.ReminderAt is null
                || item.ReminderDeliveredAt is not null)
            {
                return false;
            }

            item.ReminderDeliveredAt = deliveredAt;
            return true;
        });

    public bool Delete(string id)
        => Mutate(items => items.RemoveAll(item => item.Id == id) > 0);

    private bool Mutate(Func<List<TodoItem>, bool> mutation)
    {
        bool changed;
        lock (_gate)
        {
            List<TodoItem> next = _items.Select(Clone).ToList();
            changed = mutation(next);
            if (!changed)
                return false;

            SaveDocument(next);
            _items = next;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void LoadFromDisk()
    {
        if (TryLoad(Path, out List<TodoItem> loaded))
        {
            _items = loaded;
            return;
        }

        if (File.Exists(Path) && TryLoad(BackupPath, out loaded))
        {
            _items = loaded;
            try
            {
                SaveDocument(loaded);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                LastLoadError = $"待办已从备份读取，但恢复主文件失败：{ex.Message}";
            }

            return;
        }

        if (File.Exists(Path) || File.Exists(BackupPath))
            LastLoadError = "待办文件损坏，且没有可用备份；已从空列表开始。";
    }

    private static bool TryLoad(string path, out List<TodoItem> items)
    {
        items = [];
        if (!File.Exists(path))
            return false;

        try
        {
            TodoDocument? document = JsonSerializer.Deserialize<TodoDocument>(
                File.ReadAllText(path),
                JsonOptions);
            if (document?.Items is null)
                return false;

            items = document.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Id)
                    && !string.IsNullOrWhiteSpace(item.Title))
                .Select(Clone)
                .ToList();
            return true;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            return false;
        }
    }

    private void SaveDocument(List<TodoItem> items)
    {
        var document = new TodoDocument { Items = items.Select(Clone).ToList() };
        string json = JsonSerializer.Serialize(document, JsonOptions);
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temp = Path + ".tmp";
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
            File.Copy(temp, Path, overwrite: true);
            File.Delete(temp);
        }
    }

    private static TodoItem Clone(TodoItem item) => new()
    {
        Id = item.Id,
        Title = item.Title,
        IsCompleted = item.IsCompleted,
        CreatedAt = item.CreatedAt,
        CompletedAt = item.CompletedAt,
        ReminderAt = item.ReminderAt,
        ReminderDeliveredAt = item.ReminderDeliveredAt,
    };
}
