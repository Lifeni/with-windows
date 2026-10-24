using WithWindows.Core.Actions;

namespace WithWindows.Core.Tests;

public class DisplayModeActionTests
{
    [Theory]
    [InlineData("internal", 0x00000001u)]
    [InlineData("clone", 0x00000002u)]
    [InlineData("extend", 0x00000004u)]
    [InlineData("external", 0x00000008u)]
    public void TopologyFor_已知模式_映射为标志(string mode, uint expectedFlag)
        => Assert.Equal(expectedFlag, DisplayModeAction.TopologyFor(mode));

    [Fact]
    public void TopologyFor_未知模式_抛异常()
        => Assert.Throws<ArgumentException>(() => DisplayModeAction.TopologyFor("triple"));

    [Fact]
    public void Id_与配置工具标识一致()
        => Assert.Equal("display_mode", ((IAction)new DisplayModeAction()).Id);

    [Fact]
    public void Execute_未知模式_在改动显示状态前抛异常()
        => Assert.Throws<ArgumentException>(() => new DisplayModeAction().Execute("triple"));

    [Theory]
    [InlineData("internal", "internal", false)]
    [InlineData("extend", "internal", true)]
    [InlineData("internal", "extend", true)]
    public void Decide_显式模式_与当前比较(string current, string requested, bool expectedChange)
    {
        var (target, isChange) = DisplayModeAction.Decide(current, requested, Array.Empty<string>());
        Assert.Equal(requested, target);
        Assert.Equal(expectedChange, isChange);
    }

    [Fact]
    public void Decide_当前拓扑未知_保守执行切换()
    {
        var (target, isChange) = DisplayModeAction.Decide(null, "internal", Array.Empty<string>());
        Assert.Equal("internal", target);
        Assert.True(isChange);
    }

    [Theory]
    [InlineData("internal", "extend")]
    [InlineData("extend", "internal")]
    public void PickToggleTarget_两模式循环(string current, string expected)
        => Assert.Equal(expected, DisplayModeAction.PickToggleTarget(current, new[] { "internal", "extend" }));

    [Fact]
    public void PickToggleTarget_三模式按序循环()
    {
        string[] modes = { "internal", "clone", "extend" };
        Assert.Equal("clone", DisplayModeAction.PickToggleTarget("internal", modes));
        Assert.Equal("internal", DisplayModeAction.PickToggleTarget("extend", modes));
    }

    [Fact]
    public void PickToggleTarget_当前不在列表_回退第一项()
        => Assert.Equal("internal", DisplayModeAction.PickToggleTarget("clone", new[] { "internal", "extend" }));

    [Fact]
    public void PickToggleTarget_当前未知_返回第一项()
        => Assert.Equal("internal", DisplayModeAction.PickToggleTarget(null, new[] { "internal", "extend" }));

    [Fact]
    public void PickToggleTarget_候选为空_默认内部与扩展()
    {
        Assert.Equal("extend", DisplayModeAction.PickToggleTarget("internal", Array.Empty<string>()));
        Assert.Equal("internal", DisplayModeAction.PickToggleTarget(null, Array.Empty<string>()));
    }

    [Fact]
    public void Decide_toggle_解析目标与变化()
    {
        var (target, isChange) = DisplayModeAction.Decide("internal", "toggle", new[] { "internal", "extend" });
        Assert.Equal("extend", target);
        Assert.True(isChange);
    }

    [Theory]
    [InlineData("internal", "切换：扩展模式")]
    [InlineData("extend", "切换：当前屏幕")]
    public void ToggleMenuText_显示下一次切换目标(string current, string expected)
        => Assert.Equal(
            expected,
            DisplayModeAction.ToggleMenuText(current, new[] { "internal", "extend" }));
}
