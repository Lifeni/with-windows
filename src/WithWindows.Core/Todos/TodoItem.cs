namespace WithWindows.Core.Todos;

/// <summary>一条待办；提醒为一次性，<see cref="ReminderDeliveredAt"/> 用于避免重复触发。</summary>
public sealed class TodoItem
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public bool IsCompleted { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? ReminderAt { get; set; }
    public DateTimeOffset? ReminderDeliveredAt { get; set; }
}

/// <summary>待办文件根对象；独立于应用配置，避免设置写入影响业务数据。</summary>
public sealed class TodoDocument
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public List<TodoItem> Items { get; set; } = [];
}
