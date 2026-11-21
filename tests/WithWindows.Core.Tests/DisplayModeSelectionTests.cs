using WithWindows.Core.Config;

namespace WithWindows.Core.Tests;

public class DisplayModeSelectionTests
{
    [Theory]
    [MemberData(nameof(MinimumCountCases))]
    public void Normalize_不足两项_补齐到至少两项(string[] input, string[] expected)
        => Assert.Equal(expected, DisplayModeSelection.Normalize(input));

    public static TheoryData<string[], string[]> MinimumCountCases => new()
    {
        { Array.Empty<string>(), new[] { "internal", "extend" } },
        { new[] { "internal" }, new[] { "internal", "extend" } },
        { new[] { "external" }, new[] { "internal", "external" } },
        { new[] { "clone" }, new[] { "internal", "clone" } },
    };

    [Fact]
    public void Normalize_过滤未知值去重并保留循环顺序()
    {
        List<string> result = DisplayModeSelection.Normalize(
            new[] { "clone", "ghost", "internal", "clone", "extend" });

        Assert.Equal(new[] { "clone", "internal", "extend" }, result);
    }

    [Fact]
    public void TryChangeSelection_已有两项时取消一项_拒绝并保持原选择()
    {
        bool changed = DisplayModeSelection.TryChangeSelection(
            new[] { "internal", "extend" },
            "extend",
            isChecked: false,
            out List<string> next);

        Assert.False(changed);
        Assert.Equal(new[] { "internal", "extend" }, next);
    }

    [Fact]
    public void TryChangeSelection_已有三项时取消一项_允许()
    {
        bool changed = DisplayModeSelection.TryChangeSelection(
            new[] { "internal", "extend", "clone" },
            "extend",
            isChecked: false,
            out List<string> next);

        Assert.True(changed);
        Assert.Equal(new[] { "internal", "clone" }, next);
    }

    [Fact]
    public void TryChangeSelection_新增模式_按固定顺序插入()
    {
        bool changed = DisplayModeSelection.TryChangeSelection(
            new[] { "internal", "clone" },
            "extend",
            isChecked: true,
            out List<string> next);

        Assert.True(changed);
        Assert.Equal(new[] { "internal", "extend", "clone" }, next);
    }

    [Fact]
    public void TryChangeSelection_未知模式_拒绝()
    {
        bool changed = DisplayModeSelection.TryChangeSelection(
            new[] { "internal", "extend" },
            "ghost",
            isChecked: true,
            out List<string> next);

        Assert.False(changed);
        Assert.Equal(new[] { "internal", "extend" }, next);
    }

    [Fact]
    public void IsAtMinimum_恰好两项_返回真()
        => Assert.True(DisplayModeSelection.IsAtMinimum(new[] { "internal", "extend" }));

    [Fact]
    public void IsAtMinimum_超过两项_返回假()
        => Assert.False(DisplayModeSelection.IsAtMinimum(new[] { "internal", "extend", "clone" }));

    [Fact]
    public void IsAtMinimum_不足两项_归一后返回真()
        => Assert.True(DisplayModeSelection.IsAtMinimum(new[] { "clone" }));
}
