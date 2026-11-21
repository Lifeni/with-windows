using Microsoft.UI.Xaml;
using WithWindows.Core.Todos;

namespace WithWindows.Views;

/// <summary>待办列表中的一行展示数据。</summary>
public sealed class TodoRowViewModel
{
    public TodoRowViewModel(TodoItem item)
    {
        Id = item.Id;
        Title = item.Title;
        IsCompleted = item.IsCompleted;
        ReminderText = TodoDisplayFormatter.ReminderText(item);
        ReminderButtonText = item.ReminderAt is null ? "设置提醒" : "修改提醒";
        CompleteAutomationId = $"TodoComplete_{item.Id}";
        ReminderAutomationId = $"TodoReminder_{item.Id}";
        DeleteAutomationId = $"TodoDelete_{item.Id}";
        CompleteAutomationName = $"切换完成状态：{Title}";
        ReminderAutomationName = $"{ReminderButtonText}：{Title}";
        DeleteAutomationName = $"删除待办：{Title}";
    }

    public string Id { get; }
    public string Title { get; }
    public bool IsCompleted { get; }
    public string ReminderText { get; }
    public string ReminderButtonText { get; }
    public string CompleteAutomationId { get; }
    public string ReminderAutomationId { get; }
    public string DeleteAutomationId { get; }
    public string CompleteAutomationName { get; }
    public string ReminderAutomationName { get; }
    public string DeleteAutomationName { get; }

    public Visibility PendingVisibility =>
        IsCompleted ? Visibility.Collapsed : Visibility.Visible;

    public Visibility CompletedVisibility =>
        IsCompleted ? Visibility.Visible : Visibility.Collapsed;

    public override string ToString() => Title;
}
