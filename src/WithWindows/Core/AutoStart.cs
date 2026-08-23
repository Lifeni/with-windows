using System.Windows.Forms;
using Microsoft.Win32;

namespace WithWindows.Core;

/// <summary>
/// 开机自启动开关（HKCU\Software\Microsoft\Windows\CurrentVersion\Run，用户级、无需管理员权限）。
/// 写 exe 全路径并加引号，防止路径含空格时启动失败。
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WithWindows";

    public static bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) is string value && value.Length > 0;
    }

    public static void Enable()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, $"\"{Application.ExecutablePath}\"");
    }

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }
}
