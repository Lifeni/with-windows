using WithWindows.Core.Todos;

namespace WithWindows.Core.Tests;

public class TodoReminderNotificationFormatterTests
{
    [Fact]
    public void FormatBody_单条提醒()
        => Assert.Equal(
            "“整理发布说明”已到期。",
            TodoReminderNotificationFormatter.FormatBody(["整理发布说明"]));

    [Fact]
    public void FormatBody_多条提醒()
        => Assert.Equal(
            "2 项待办已到期：整理发布说明；检查安装包",
            TodoReminderNotificationFormatter.FormatBody(
                ["整理发布说明", "检查安装包"]));

    [Fact]
    public void FormatBody_没有有效标题时使用通用文案()
        => Assert.Equal(
            "有一项待办已到期。",
            TodoReminderNotificationFormatter.FormatBody(["", "  "]));

    [Fact]
    public void FormatBody_正文不超过系统通知上限()
    {
        string title = new('待', TodoReminderNotificationFormatter.MaximumBodyLength * 2);

        string body = TodoReminderNotificationFormatter.FormatBody([title]);

        Assert.Equal(TodoReminderNotificationFormatter.MaximumBodyLength, body.Length);
        Assert.EndsWith("…", body);
    }
}
