using System.Runtime.InteropServices;
using Microsoft.Win32;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Actions;

/// <summary>
/// 切换 Windows 亮色/暗色模式（应用与系统外观同步切换），基于注册表
/// HKCU\...\Themes\Personalize\AppsUseLightTheme / SystemUsesLightTheme 写入，并广播
/// WM_SETTINGCHANGE("ImmersiveColorSet") 让正在运行的应用即时刷新，无需重启资源管理器。
/// args 支持 {"mode": "light"} 或字符串 "light"；mode 取值：light / dark，以及 "toggle"：
/// 读取当前模式并切换到相反值。目标模式与当前一致时返回 Changed=false，宿主不弹通知。
/// </summary>
public sealed class ThemeAction : IAction
{
    /// <summary>主题注册表键（HKCU 完整路径，GetValue/SetValue 通用；SetValue 会自动建键）。</summary>
    internal const string PersonalizeKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string AppsUseLightTheme = "AppsUseLightTheme";
    private const string SystemUsesLightTheme = "SystemUsesLightTheme";

    public string Name => "theme";

    public ActionResult Execute(object? args)
    {
        string requested = ParseArgs(args);
        var (target, isChange) = Decide(GetCurrentMode(), requested);

        if (!isChange)
            return new ActionResult(false, $"当前已是{ModeDisplayName(target)}");

        Apply(target);
        return new ActionResult(true, $"已切换主题模式：{ModeDisplayName(target)}");
    }

    /// <summary>读取当前应用主题：AppsUseLightTheme（1=亮色，0=暗色）。无法读取时返回 null。</summary>
    internal static string? GetCurrentMode()
    {
        try
        {
            object? value = Registry.GetValue(PersonalizeKey, AppsUseLightTheme, null);
            return value is int i ? (i != 0 ? "light" : "dark") : null;
        }
        catch
        {
            // 读取失败返回 null，调用方按"无法比较"保守处理
            return null;
        }
    }

    /// <summary>决定目标模式与是否真正变化。纯函数，便于测试。</summary>
    internal static (string Target, bool IsChange) Decide(string? current, string requested)
    {
        string target = requested == "toggle" ? PickToggleTarget(current) : requested;
        // 当前模式无法读取（null）时：显式模式保守执行；toggle 无法确定切换方向，由 PickToggleTarget 报错
        return (target, current is null || target != current);
    }

    /// <summary>toggle 目标选择：亮 ↔ 暗。当前状态未知时无法确定方向，抛错由宿主气泡提示。</summary>
    internal static string PickToggleTarget(string? current) => current switch
    {
        "light" => "dark",
        "dark" => "light",
        _ => throw new InvalidOperationException("无法读取当前主题状态"),
    };

    internal static string ParseArgs(object? args)
    {
        if (args is string direct && !string.IsNullOrWhiteSpace(direct))
            return direct.Trim().ToLowerInvariant();

        throw new ArgumentException("缺少 args.mode（light|dark|toggle）");
    }

    /// <summary>写入应用与系统主题注册表并广播 WM_SETTINGCHANGE，运行中的应用即时切换。</summary>
    internal static void Apply(string mode)
    {
        int lightValue = mode == "dark" ? 0 : 1;
        try
        {
            Registry.SetValue(PersonalizeKey, AppsUseLightTheme, lightValue, RegistryValueKind.DWord);
            Registry.SetValue(PersonalizeKey, SystemUsesLightTheme, lightValue, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"写入主题注册表失败：{ex.Message}", ex);
        }

        // "ImmersiveColorSet"：通知运行中的应用刷新主题。SMTO_ABORTIFHUNG 避免被挂起窗口阻塞
        IntPtr lParam = Marshal.StringToHGlobalAuto("ImmersiveColorSet");
        try
        {
            NativeMethods.SendMessageTimeout(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
                UIntPtr.Zero, lParam, NativeMethods.SMTO_ABORTIFHUNG, 1000, out _);
        }
        finally
        {
            Marshal.FreeHGlobal(lParam);
        }
    }

    private static string ModeDisplayName(string mode) => mode switch
    {
        "light" => "亮色",
        "dark" => "暗色",
        _ => mode,
    };
}
