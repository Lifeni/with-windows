using WithWindows.Core.App;

namespace WithWindows.Core.Tests;

public class WindowPlacementTests
{
    [Fact]
    public void IsUsable_最小化占位矩形_为假()
        => Assert.False(WindowPlacement.IsUsable(x: -32000, y: -32000, width: 158, height: 26));

    [Fact]
    public void IsUsable_哨兵边界_为假()
        => Assert.False(WindowPlacement.IsUsable(x: WindowPlacement.MinimizedSentinel, y: 100, width: 800, height: 600));

    [Fact]
    public void IsUsable_副屏上的负坐标_为真()
        => Assert.True(WindowPlacement.IsUsable(x: -1920, y: 120, width: 1100, height: 720));

    [Fact]
    public void IsUsable_宽高为零_为假()
        => Assert.False(WindowPlacement.IsUsable(x: 100, y: 100, width: 0, height: 600));

    [Fact]
    public void IsUsable_宽高为负_为假()
        => Assert.False(WindowPlacement.IsUsable(x: 100, y: 100, width: 800, height: -1));

    [Fact]
    public void CanPersist_窗口状态尚未恢复_为假()
        => Assert.False(WindowPlacement.CanPersist(
            stateRestored: false,
            x: 100,
            y: 100,
            width: 800,
            height: 600));

    [Fact]
    public void CanPersist_窗口状态已恢复且矩形可用_为真()
        => Assert.True(WindowPlacement.CanPersist(
            stateRestored: true,
            x: 100,
            y: 100,
            width: 800,
            height: 600));
}
