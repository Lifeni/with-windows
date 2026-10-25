namespace WithWindows.Core.Todos;

/// <summary>一次到期提醒在 UI 线程上的处理结果。</summary>
public enum TodoReminderDeliveryResult
{
    Deferred,
    Delivered,
    Failed,
}
