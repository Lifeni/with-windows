using WithWindows.Core.Config;
using WithWindows.Core.Interop;
using WithWindows.Core.Theme;

namespace WithWindows.Core.Actions;

/// <summary>
/// 切换整个 Windows 亮暗主题。toggle 在当前主题与另一主题之间循环；light / dark 供定时任务显式指定。
/// 配置了对应壁纸时同步应用壁纸，否则保留当前桌面壁纸。
/// </summary>
public sealed class ThemeToggleAction : IAction
{
    private readonly ThemeSwitchConfig _config;
    private readonly Func<ThemeMode> _readCurrent;
    private readonly Action<ThemeMode, string?> _apply;

    public ThemeToggleAction(
        ThemeSwitchConfig config,
        Func<ThemeMode>? readCurrent = null,
        Action<ThemeMode, string?>? apply = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _readCurrent = readCurrent ?? SystemThemeInterop.GetCurrentMode;
        _apply = apply ?? SystemThemeInterop.Apply;
    }

    public string Id => "theme_toggle";

    public ActionResult Execute(string argument)
    {
        string requested = argument.Trim().ToLowerInvariant();
        if (requested is not ("toggle" or "light" or "dark"))
            throw new ArgumentException(
                $"未知主题切换方式“{requested}”（支持 toggle|light|dark）");

        ThemeMode current = ReadCurrentOrDefault(_readCurrent);
        ThemeMode target = requested == "toggle"
            ? ThemeSwitchPolicy.Next(current)
            : requested == "light" ? ThemeMode.Light : ThemeMode.Dark;

        string? configuredWallpaper = target == ThemeMode.Light
            ? _config.LightWallpaper
            : _config.DarkWallpaper;
        string? wallpaper = WallpaperStore.ExistingOrNull(configuredWallpaper);
        _apply(target, wallpaper);

        bool changed = target != current;
        string message = changed
            ? $"已切换系统主题：{ThemeSwitchPolicy.DisplayName(target)}"
            : $"当前已是{ThemeSwitchPolicy.DisplayName(target)}";
        if (!string.IsNullOrWhiteSpace(configuredWallpaper) && wallpaper is null)
            message += "；壁纸文件不存在，已保留当前壁纸";
        return new ActionResult(changed, message);
    }

    /// <summary>托盘菜单显示下一次 toggle 的目标，固定四个汉字。</summary>
    public string GetToggleMenuText()
        => ThemeSwitchPolicy.MenuTargetText(ReadCurrentOrDefault(_readCurrent));

    /// <summary>读取当前主题；失败按亮色处理，则下一次循环目标是暗色。</summary>
    public static ThemeMode CurrentModeOrDefault()
        => ReadCurrentOrDefault(SystemThemeInterop.GetCurrentMode);

    /// <summary>读取当前主题；注册表异常时按亮色处理。</summary>
    internal static ThemeMode ReadCurrentOrDefault(Func<ThemeMode> readCurrent)
    {
        try
        {
            return readCurrent();
        }
        catch
        {
            return ThemeMode.Light;
        }
    }

}
