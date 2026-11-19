using WithWindows.Core.Theme;

namespace WithWindows.Core.Tests;

public class ThemeSwitchPolicyTests
{
    [Theory]
    [InlineData(ThemeMode.Light, ThemeMode.Dark)]
    [InlineData(ThemeMode.Dark, ThemeMode.Light)]
    public void Next_在亮暗之间循环(ThemeMode current, ThemeMode expected)
        => Assert.Equal(expected, ThemeSwitchPolicy.Next(current));

    [Theory]
    [InlineData(ThemeMode.Light, "暗色模式")]
    [InlineData(ThemeMode.Dark, "亮色模式")]
    public void MenuTargetText_显示下一次目标且固定四个汉字(
        ThemeMode current,
        string expected)
    {
        string text = ThemeSwitchPolicy.MenuTargetText(current);
        Assert.Equal(expected, text);
        Assert.Equal(4, text.Length);
    }

    [Theory]
    [InlineData(ThemeMode.Light, "亮色模式")]
    [InlineData(ThemeMode.Dark, "暗色模式")]
    public void DisplayName_返回完整模式名(ThemeMode mode, string expected)
        => Assert.Equal(expected, ThemeSwitchPolicy.DisplayName(mode));
}
