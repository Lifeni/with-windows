using System.Runtime.InteropServices;
using WithWindows.Actions;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Tests;

public class DisplayModeActionTests
{
    [Theory]
    [InlineData("internal", 0x00000001)]
    [InlineData("clone", 0x00000002)]
    [InlineData("extend", 0x00000004)]
    [InlineData("external", 0x00000008)]
    public void TopologyFor_KnownModes_MapsToFlag(string mode, uint expectedFlag)
    {
        Assert.Equal(expectedFlag, DisplayModeAction.TopologyFor(mode));
    }

    [Fact]
    public void TopologyFor_UnknownMode_Throws()
    {
        Assert.Throws<ArgumentException>(() => DisplayModeAction.TopologyFor("triple"));
    }

    [Fact]
    public void Execute_UnknownMode_ThrowsBeforeChangingState()
    {
        // 非法模式在验证阶段抛出，不触碰显示器状态
        Assert.Throws<ArgumentException>(() => new DisplayModeAction().Execute("triple"));
    }

    [Theory]
    [InlineData("internal", "internal", false)]
    [InlineData("extend", "internal", true)]
    [InlineData("internal", "extend", true)]
    public void Decide_ExplicitMode_ComparesWithCurrent(string current, string requested, bool expectedChange)
    {
        var (target, isChange) = DisplayModeAction.Decide(current, requested, Array.Empty<string>());

        Assert.Equal(requested, target);
        Assert.Equal(expectedChange, isChange);
    }

    [Fact]
    public void Decide_UnknownCurrent_ConservativelyApplies()
    {
        var (target, isChange) = DisplayModeAction.Decide(null, "internal", Array.Empty<string>());

        Assert.Equal("internal", target);
        Assert.True(isChange);
    }

    [Theory]
    [InlineData("internal", "extend")]
    [InlineData("extend", "internal")]
    public void PickToggleTarget_TwoModeList_CyclesToOther(string current, string expected)
    {
        string[] modes = { "internal", "extend" };

        Assert.Equal(expected, DisplayModeAction.PickToggleTarget(current, modes));
    }

    [Fact]
    public void PickToggleTarget_ThreeModes_CyclesInOrder()
    {
        string[] modes = { "internal", "clone", "extend" };

        Assert.Equal("clone", DisplayModeAction.PickToggleTarget("internal", modes));
        Assert.Equal("internal", DisplayModeAction.PickToggleTarget("extend", modes));
    }

    [Fact]
    public void PickToggleTarget_CurrentNotInList_FallsBackToFirst()
    {
        string[] modes = { "internal", "extend" };

        Assert.Equal("internal", DisplayModeAction.PickToggleTarget("clone", modes));
    }

    [Fact]
    public void PickToggleTarget_UnknownCurrent_ReturnsFirst()
    {
        Assert.Equal("internal", DisplayModeAction.PickToggleTarget(null, new[] { "internal", "extend" }));
    }

    [Fact]
    public void PickToggleTarget_EmptyModes_DefaultsToInternalExtend()
    {
        Assert.Equal("extend", DisplayModeAction.PickToggleTarget("internal", Array.Empty<string>()));
        Assert.Equal("internal", DisplayModeAction.PickToggleTarget(null, Array.Empty<string>()));
    }

    [Fact]
    public void Decide_ToggleMode_ResolvesTargetAndChange()
    {
        string[] modes = { "internal", "extend" };

        var (target, isChange) = DisplayModeAction.Decide("internal", "toggle", modes);

        Assert.Equal("extend", target);
        Assert.True(isChange);
    }

}

public class DisplayTopologyTests
{
    [Theory]
    [InlineData(1u, "internal")]
    [InlineData(2u, "clone")]
    [InlineData(4u, "extend")]
    [InlineData(8u, "external")]
    [InlineData(0u, null)]
    [InlineData(3u, null)]
    public void TopologyName_MapsKnownIds(uint id, string? expected)
    {
        Assert.Equal(expected, DisplayTopology.TopologyName(id));
    }

    [Fact]
    public void GetCurrentMode_ReturnsKnownModeOrNull()
    {
        // 只读查询，不修改显示状态；任何机器上都应返回合法拓扑名或 null（查询失败）
        string? mode = DisplayTopology.GetCurrentMode();

        Assert.True(mode is null
            or "internal" or "clone" or "extend" or "external",
            $"实际值: {mode ?? "<null>"}");
    }
}

public class DisplayConfigLayoutTests
{
    // CCD 结构体布局必须与原生定义一致，否则系统写入会越界（曾导致 testhost 偶发崩溃）
    [Theory]
    [InlineData(typeof(DisplayConfigPathInfo), 72)]
    [InlineData(typeof(DisplayConfigModeInfo), 64)]
    public void StructSizes_MatchNativeLayout(Type type, int expectedSize)
    {
        Assert.Equal(expectedSize, Marshal.SizeOf(type));
    }
}


