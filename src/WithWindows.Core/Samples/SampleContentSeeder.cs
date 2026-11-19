using System.Text.Json;
using WithWindows.Core.Scheduling;
using WithWindows.Core.Todos;

namespace WithWindows.Core.Samples;

/// <summary>首次内容初始化结果；<see cref="AlreadyInitialized"/> 为 true 时跳过全部写入。</summary>
public sealed record SampleContentSeedResult(
    bool AlreadyInitialized,
    int TodosAdded,
    int TasksAdded)
{
    public bool SeededAnything => TodosAdded > 0 || TasksAdded > 0;
}

/// <summary>
/// 在示例内容初始化首次执行时写入示例待办与脚本。已有数据、备份或初始化标记时不覆盖用户内容；
/// 标记完成后，即使用户删空列表也不会再次写入示例。
/// </summary>
public static class SampleContentSeeder
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static SampleContentSeedResult EnsureInitialContent(
        string todoPath,
        string tasksPath,
        string statePath,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(todoPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(tasksPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(statePath);

        if (File.Exists(statePath))
            return new SampleContentSeedResult(
                AlreadyInitialized: true,
                TodosAdded: 0,
                TasksAdded: 0);

        int todosAdded = SeedTodosIfMissing(todoPath, now);
        int tasksAdded = SeedTasksIfMissing(tasksPath, now);
        WriteState(statePath, now);
        return new SampleContentSeedResult(
            AlreadyInitialized: false,
            TodosAdded: todosAdded,
            TasksAdded: tasksAdded);
    }

    private static int SeedTodosIfMissing(string path, DateTimeOffset now)
    {
        if (HasStoreData(path))
            return 0;

        var store = new TodoStore(path);
        if (store.LastLoadError is { } error)
            throw new InvalidOperationException(error);

        IReadOnlyList<string> titles = SampleContent.CreateTodoTitles();
        store.AddRange(titles, now);
        return titles.Count;
    }

    private static int SeedTasksIfMissing(string path, DateTimeOffset now)
    {
        if (HasStoreData(path))
            return 0;

        var store = new ScheduledTaskStore(path);
        if (store.LoadState != StoreLoadState.Empty)
            throw new InvalidOperationException(store.LastLoadError ?? "脚本数据不是空状态。");

        IReadOnlyList<ScheduledTask> tasks = SampleContent.CreateTasks(now);
        store.AddRange(tasks);
        return tasks.Count;
    }

    private static bool HasStoreData(string path)
        => File.Exists(path) || File.Exists(path + ".bak");

    private static void WriteState(string path, DateTimeOffset now)
    {
        string? directory = System.IO.Path.GetDirectoryName(path);
        if (directory is not null)
            Directory.CreateDirectory(directory);

        string temp = path + ".tmp";
        string json = JsonSerializer.Serialize(
            new SampleContentState
            {
                Version = SampleContentState.CurrentVersion,
                SeededAt = now,
            },
            JsonOptions);
        try
        {
            File.WriteAllText(temp, json);
            File.Move(temp, path);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }
}

/// <summary>示例内容初始化标记；文件存在即视为只处理过一次。</summary>
public sealed class SampleContentState
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset SeededAt { get; set; }
}
