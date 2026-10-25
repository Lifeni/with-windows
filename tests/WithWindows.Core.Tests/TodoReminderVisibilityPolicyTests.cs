using WithWindows.Core.Todos;

namespace WithWindows.Core.Tests;

public class TodoReminderVisibilityPolicyTests
{
    [Theory]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    public void CanNotify_仅在窗口显示且未最小化时返回真(
        bool windowVisible,
        bool windowMinimized,
        bool expected)
    {
        Assert.Equal(
            expected,
            TodoReminderVisibilityPolicy.CanNotify(windowVisible, windowMinimized));
    }
}
