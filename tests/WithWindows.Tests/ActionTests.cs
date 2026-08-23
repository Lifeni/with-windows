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
    public void ParseArgs_StringArg_IsTrimmedAndLowercased()
    {
        Assert.Equal(("extend", Array.Empty<string>()), DisplayModeAction.ParseArgs(" Extend "));
    }

    [Fact]
    public void ParseArgs_JsonObjectArg_ReadsModeProperty()
    {
        var json = MiniJson.Parse("""{ "mode": "extend" }""");

        Assert.Equal(("extend", Array.Empty<string>()), DisplayModeAction.ParseArgs(json));
    }

    [Fact]
    public void ParseArgs_JsonWithToggleModes_ReadsBoth()
    {
        var json = MiniJson.Parse("""{ "mode": "toggle", "modes": ["internal", "extend"] }""");

        var (mode, modes) = DisplayModeAction.ParseArgs(json);

        Assert.Equal("toggle", mode);
        Assert.Equal(new[] { "internal", "extend" }, modes);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ParseArgs_MissingOrBlank_Throws(string? input)
    {
        Assert.Throws<ArgumentException>(() => DisplayModeAction.ParseArgs(input));
    }

    [Fact]
    public void ParseArgs_JsonWithoutMode_Throws()
    {
        // MiniJson 仅支持对象/数组/字符串，这里用字符串值构造"缺 mode"的对象
        var json = MiniJson.Parse("""{ "other": "x" }""");

        Assert.Throws<ArgumentException>(() => DisplayModeAction.ParseArgs(json));
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

    [Fact]
    public void Action_ExposesExpectedName()
    {
        Assert.Equal("display_mode", new DisplayModeAction().Name);
    }
}

public class ThemeActionTests
{
    [Fact]
    public void Action_ExposesExpectedName()
    {
        Assert.Equal("theme", new ThemeAction().Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void ParseArgs_MissingOrBlank_Throws(string? input)
    {
        Assert.Throws<ArgumentException>(() => ThemeAction.ParseArgs(input));
    }

    [Fact]
    public void ParseArgs_StringArg_IsTrimmedAndLowercased()
    {
        Assert.Equal("dark", ThemeAction.ParseArgs(" Dark "));
    }

    [Fact]
    public void ParseArgs_JsonObjectArg_ReadsModeProperty()
    {
        var json = MiniJson.Parse("""{ "mode": "light" }""");

        Assert.Equal("light", ThemeAction.ParseArgs(json));
    }

    [Fact]
    public void ParseArgs_JsonWithoutMode_Throws()
    {
        var json = MiniJson.Parse("""{ "other": "x" }""");

        Assert.Throws<ArgumentException>(() => ThemeAction.ParseArgs(json));
    }

    [Theory]
    [InlineData("light", "light", false)]
    [InlineData("dark", "light", true)]
    [InlineData("light", "dark", true)]
    public void Decide_ExplicitMode_ComparesWithCurrent(string current, string requested, bool expectedChange)
    {
        var (target, isChange) = ThemeAction.Decide(current, requested);

        Assert.Equal(requested, target);
        Assert.Equal(expectedChange, isChange);
    }

    [Fact]
    public void Decide_UnknownCurrent_ConservativelyApplies()
    {
        var (target, isChange) = ThemeAction.Decide(null, "dark");

        Assert.Equal("dark", target);
        Assert.True(isChange);
    }

    [Theory]
    [InlineData("light", "dark")]
    [InlineData("dark", "light")]
    public void PickToggleTarget_FlipsMode(string current, string expected)
    {
        Assert.Equal(expected, ThemeAction.PickToggleTarget(current));
    }

    [Fact]
    public void PickToggleTarget_UnknownCurrent_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => ThemeAction.PickToggleTarget(null));
    }

    [Fact]
    public void Decide_ToggleMode_ResolvesOpposite()
    {
        var (target, isChange) = ThemeAction.Decide("light", "toggle");

        Assert.Equal("dark", target);
        Assert.True(isChange);
    }

    [Fact]
    public void GetCurrentMode_ReturnsLightOrDarkOrNull()
    {
        // 只读注册表查询，不修改主题状态；任何机器上都应返回合法值或 null（读取失败）
        string? mode = ThemeAction.GetCurrentMode();

        Assert.True(mode is null or "light" or "dark",
            $"实际值: {mode ?? "<null>"}");
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

public class ActionRegistryTests
{
    [Fact]
    public void Find_RegisteredAction_ReturnsInstance()
    {
        var registry = new ActionRegistry();
        var action = new DisplayModeAction();
        registry.Register(action);

        Assert.Same(action, registry.Find("display_mode"));
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var registry = new ActionRegistry();
        registry.Register(new DisplayModeAction());

        Assert.NotNull(registry.Find("DISPLAY_MODE"));
        Assert.NotNull(registry.Find("Display_Mode"));
    }

    [Fact]
    public void Find_UnknownAction_ReturnsNull()
    {
        var registry = new ActionRegistry();

        Assert.Null(registry.Find("no_such_action"));
    }

    [Fact]
    public void Register_SameName_Overwrites()
    {
        var registry = new ActionRegistry();
        var first = new DisplayModeAction();
        var second = new DisplayModeAction();
        registry.Register(first);
        registry.Register(second);

        Assert.Same(second, registry.Find("display_mode"));
    }
}
