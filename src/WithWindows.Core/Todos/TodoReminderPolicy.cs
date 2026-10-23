namespace WithWindows.Core.Todos;

/// <summary>提醒候选和下一次唤醒时间的纯逻辑。</summary>
public static class TodoReminderPolicy
{
    /// <summary>返回已经到期、尚未提醒且未完成的待办，按提醒时间升序。</summary>
    public static IReadOnlyList<TodoItem> GetDue(
        IEnumerable<TodoItem> items,
        DateTimeOffset now)
        => items
            .Where(IsPending)
            .Where(item => item.ReminderAt <= now)
            .OrderBy(item => item.ReminderAt)
            .ToList();

    /// <summary>返回最近一次需要唤醒的时间；没有待提醒项时为 null。</summary>
    public static DateTimeOffset? NextWakeAt(
        IEnumerable<TodoItem> items,
        DateTimeOffset now)
        => items
            .Where(IsPending)
            .Select(item => item.ReminderAt)
            .Min();

    private static bool IsPending(TodoItem item)
        => !item.IsCompleted
            && item.ReminderAt is not null
            && item.ReminderDeliveredAt is null;
}
