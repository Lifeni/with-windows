using WithWindows.Core.Todos;

namespace WithWindows.Core.Tests;

public class TodoReminderPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 23, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void GetDue_只返回未完成且尚未提醒的到期项()
    {
        TodoItem due = Item("due", Now.AddMinutes(-1));
        TodoItem future = Item("future", Now.AddMinutes(1));
        TodoItem completed = Item("completed", Now.AddMinutes(-2));
        completed.IsCompleted = true;
        TodoItem delivered = Item("delivered", Now.AddMinutes(-3));
        delivered.ReminderDeliveredAt = Now.AddMinutes(-2);

        IReadOnlyList<TodoItem> result =
            TodoReminderPolicy.GetDue([due, future, completed, delivered], Now);

        Assert.Equal(["due"], result.Select(item => item.Id));
    }

    [Fact]
    public void NextWakeAt_返回最早待提醒时间()
    {
        TodoItem later = Item("later", Now.AddHours(2));
        TodoItem sooner = Item("sooner", Now.AddHours(1));

        DateTimeOffset? next = TodoReminderPolicy.NextWakeAt([later, sooner], Now);

        Assert.Equal(Now.AddHours(1), next);
    }

    [Fact]
    public void NextWakeAt_没有候选时返回空()
    {
        TodoItem completed = Item("completed", Now.AddHours(1));
        completed.IsCompleted = true;

        Assert.Null(TodoReminderPolicy.NextWakeAt([completed], Now));
    }

    [Fact]
    public void ReminderText_已提醒优先显示已提醒()
    {
        TodoItem item = Item("item", Now.AddHours(1));
        item.ReminderDeliveredAt = Now;

        string text = TodoDisplayFormatter.ReminderText(item);

        Assert.Equal("已提醒", text);
    }

    [Fact]
    public void ReminderText_中文与数字之间保留空格()
    {
        DateTime local = new(2026, 10, 23, 18, 30, 0, DateTimeKind.Unspecified);
        TodoItem item = Item("item", new DateTimeOffset(local));

        string text = TodoDisplayFormatter.ReminderText(item);

        Assert.Equal("提醒：10 月 23 日 18:30", text);
    }

    private static TodoItem Item(string id, DateTimeOffset reminderAt) => new()
    {
        Id = id,
        Title = id,
        CreatedAt = Now,
        ReminderAt = reminderAt,
    };
}
