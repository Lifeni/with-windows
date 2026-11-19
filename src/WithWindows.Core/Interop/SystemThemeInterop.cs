using System.Globalization;
using Microsoft.Win32;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;
using WithWindows.Core.Theme;

namespace WithWindows.Core.Interop;

/// <summary>
/// Windows 亮暗主题与桌面壁纸的系统适配。主题写入当前用户的 Personalize 注册表并广播设置变化；
/// 壁纸通过 SystemParametersInfo 只替换图片，保留系统当前的契合度和平铺位置，
/// 不改变图片本身的文件内容。
/// </summary>
public static class SystemThemeInterop
{
    private const string PersonalizeKey =
        @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private static readonly object ApplyGate = new();

    /// <summary>读取当前系统主题；注册表缺失或类型异常时按亮色处理。</summary>
    public static ThemeMode GetCurrentMode()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        object? raw = key?.GetValue("SystemUsesLightTheme")
            ?? key?.GetValue("AppsUseLightTheme");
        return Convert.ToInt32(raw ?? 1, CultureInfo.InvariantCulture) != 0
            ? ThemeMode.Light
            : ThemeMode.Dark;
    }

    /// <summary>应用整个 Windows 主题；有可用壁纸时同时应用壁纸。失败抛异常由宿主记录。</summary>
    public static void Apply(ThemeMode mode, string? wallpaperPath)
    {
        lock (ApplyGate)
        {
            int value = mode == ThemeMode.Light ? 1 : 0;
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                PersonalizeKey,
                writable: true))
            {
                key.SetValue("AppsUseLightTheme", value, RegistryValueKind.DWord);
                key.SetValue("SystemUsesLightTheme", value, RegistryValueKind.DWord);
            }

            BroadcastSettingChange("ImmersiveColorSet");
            if (wallpaperPath is not null)
                ApplyWallpaper(wallpaperPath);
        }
    }

    private static unsafe void ApplyWallpaper(string path)
    {
        // 不覆写 WallpaperStyle / TileWallpaper：Windows 把这些值作为当前壁纸的
        // 契合度和平铺方式保存，主题切换只换图片，应保留用户已经选好的位置。

        fixed (char* pathPointer = path)
        {
            SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS flags =
                SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_UPDATEINIFILE
                | SYSTEM_PARAMETERS_INFO_UPDATE_FLAGS.SPIF_SENDCHANGE;
            if (!PInvoke.SystemParametersInfo(
                SYSTEM_PARAMETERS_INFO_ACTION.SPI_SETDESKWALLPAPER,
                0,
                pathPointer,
                flags))
            {
                throw new InvalidOperationException("设置桌面壁纸失败。");
            }
        }
    }

    private static unsafe void BroadcastSettingChange(string setting)
    {
        fixed (char* settingPointer = setting)
        {
            // SendNotifyMessage 不等待各窗口处理 WM_SETTINGCHANGE。此前使用
            // SendMessageTimeout 逐个等待桌面、Shell 与各应用，实测单次广播约 1 秒，
            // 从托盘或热键切换主题时会把 UI 线程一起卡住。
            PInvoke.SendNotifyMessage(
                new HWND(new IntPtr(0xffff)),
                Win32Constants.WM_SETTINGCHANGE,
                default,
                (nint)settingPointer);
        }
    }
}
