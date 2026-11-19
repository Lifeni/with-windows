using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Core.Theme;

namespace WithWindows.Core.Tests;

public sealed class ThemeToggleActionTests
{
    [Fact]
    public void Id_与配置工具标识一致()
        => Assert.Equal("theme_toggle", ((IAction)new ThemeToggleAction(new ThemeSwitchConfig())).Id);

    [Fact]
    public void Execute_toggle_从亮色切到暗色()
    {
        ThemeMode applied = ThemeMode.Light;
        var action = new ThemeToggleAction(
            new ThemeSwitchConfig(),
            readCurrent: () => ThemeMode.Light,
            apply: (mode, _) => applied = mode);

        ActionResult result = action.Execute("toggle");

        Assert.True(result.Changed);
        Assert.Equal(ThemeMode.Dark, applied);
        Assert.Contains("暗色模式", result.Message);
    }

    [Fact]
    public void Execute_toggle_读取当前主题失败时按亮色处理()
    {
        ThemeMode applied = ThemeMode.Light;
        var action = new ThemeToggleAction(
            new ThemeSwitchConfig(),
            readCurrent: () => throw new InvalidOperationException("注册表不可用"),
            apply: (mode, _) => applied = mode);

        Assert.True(action.Execute("toggle").Changed);
        Assert.Equal(ThemeMode.Dark, applied);
    }

    [Fact]
    public void Execute_显式目标已经生效_返回未变化但仍应用壁纸()
    {
        string wallpaper = Path.GetTempFileName();
        File.WriteAllText(wallpaper, "image");
        string? appliedWallpaper = null;
        var action = new ThemeToggleAction(
            new ThemeSwitchConfig { LightWallpaper = wallpaper },
            readCurrent: () => ThemeMode.Light,
            apply: (_, wallpaper) => appliedWallpaper = wallpaper);

        try
        {
            ActionResult result = action.Execute("light");

            Assert.False(result.Changed);
            Assert.Equal(wallpaper, appliedWallpaper);
            Assert.Contains("当前已是亮色模式", result.Message);
        }
        finally
        {
            File.Delete(wallpaper);
        }
    }

    [Fact]
    public void Execute_未知参数_在应用系统设置前抛异常()
    {
        bool applied = false;
        var action = new ThemeToggleAction(
            new ThemeSwitchConfig(),
            readCurrent: () => ThemeMode.Light,
            apply: (_, _) => applied = true);

        Assert.Throws<ArgumentException>(() => action.Execute("auto"));
        Assert.False(applied);
    }

    [Fact]
    public void GetToggleMenuText_显示下一次目标()
    {
        var action = new ThemeToggleAction(
            new ThemeSwitchConfig(),
            readCurrent: () => ThemeMode.Dark,
            apply: (_, _) => { });

        Assert.Equal("亮色模式", action.GetToggleMenuText());
    }

    [Fact]
    public void GetToggleMenuText_读取失败时目标为暗色()
    {
        var action = new ThemeToggleAction(
            new ThemeSwitchConfig(),
            readCurrent: () => throw new InvalidOperationException("注册表不可用"),
            apply: (_, _) => { });

        Assert.Equal("暗色模式", action.GetToggleMenuText());
    }
}
