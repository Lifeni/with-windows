using WithWindows.Core.Config;

namespace WithWindows.Core.Tests;

public class DisplayModesTests
{
    [Fact]
    public void All_保持固定顺序且标识唯一()
    {
        Assert.Equal(
            new[] { "internal", "extend", "external", "clone" },
            DisplayModes.All.Select(mode => mode.Id));
        Assert.Equal(
            DisplayModes.All.Count,
            DisplayModes.All.Select(mode => mode.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(
            DisplayModes.All.Count,
            DisplayModes.All.Select(mode => mode.DisplayName).Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void MenuName_全部为四个汉字()
    {
        Assert.All(
            DisplayModes.All,
            mode =>
            {
                Assert.Equal(4, mode.MenuName.Length);
                Assert.DoesNotContain(' ', mode.MenuName);
            });
    }

    [Theory]
    [InlineData("internal", "仅当前屏幕", "当前屏幕")]
    [InlineData("extend", "扩展模式", "扩展模式")]
    [InlineData("external", "仅外接屏幕", "外接屏幕")]
    [InlineData("clone", "复制模式", "复制模式")]
    public void DisplayName_与MenuName_按标识返回元数据(
        string id,
        string displayName,
        string menuName)
    {
        Assert.Equal(displayName, DisplayModes.DisplayName(id));
        Assert.Equal(menuName, DisplayModes.MenuName(id));
    }

    [Fact]
    public void 未知标识_原样返回()
    {
        Assert.Equal("ghost", DisplayModes.DisplayName("ghost"));
        Assert.Equal("ghost", DisplayModes.MenuName("ghost"));
    }
}
