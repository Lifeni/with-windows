namespace WithWindows.Core.Todos;

/// <summary>待办提醒只在主窗口实际显示且未最小化时呈现。</summary>
public static class TodoReminderVisibilityPolicy
{
    public static bool CanNotify(bool windowVisible, bool windowMinimized)
        => windowVisible && !windowMinimized;
}
