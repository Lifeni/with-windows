using WithWindows.Core.App;

namespace WithWindows.Core.Tests;

public class AutoStartTests
{
    [Fact]
    public void IsEnabled_只读查询_不抛异常()
    {
        bool enabled = AutoStart.IsEnabled();
        Assert.True(enabled || !enabled);
    }
}
