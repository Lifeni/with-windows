namespace WithWindows.Core.Theme;

/// <summary>亮暗主题切换的纯逻辑：下一次目标、界面名称与托盘四字文案。</summary>
public static class ThemeSwitchPolicy
{
    /// <summary>循环切换的下一个主题。</summary>
    public static ThemeMode Next(ThemeMode current)
        => current == ThemeMode.Light ? ThemeMode.Dark : ThemeMode.Light;

    /// <summary>界面中的完整主题名。</summary>
    public static string DisplayName(ThemeMode mode)
        => mode == ThemeMode.Light ? "亮色模式" : "暗色模式";

    /// <summary>托盘二级菜单显示的下一次切换目标，固定四个汉字。</summary>
    public static string MenuTargetText(ThemeMode current)
        => DisplayName(Next(current));
}
