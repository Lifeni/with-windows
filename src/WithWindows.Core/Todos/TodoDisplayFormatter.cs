namespace WithWindows.Core.Todos;

/// <summary>待办列表中适合直接展示的中文时间摘要。</summary>
public static class TodoDisplayFormatter
{
    public static string ReminderText(TodoItem item)
        => item.ReminderDeliveredAt is not null
            ? "已提醒"
            : item.ReminderAt is { } reminderAt
                ? $"提醒：{reminderAt.ToLocalTime():M月 d 日 HH:mm}"
                : "未设置提醒";
}
