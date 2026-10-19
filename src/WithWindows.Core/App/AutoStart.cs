using Microsoft.Win32;

namespace WithWindows.Core.App;

/// <summary>
/// 开机自启动开关（HKCU\Software\Microsoft\Windows\CurrentVersion\Run，用户级、无需管理员权限）。
/// 写 exe 全路径并加引号（防止路径含空格时启动失败），再带 <c>--tray</c>：开机拉起后只驻留托盘，不弹主窗口。
/// </summary>
public static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "WithWindows";

    public static bool IsEnabled()
        => Read() is { Length: > 0 };

    public static void Enable()
        => Write();

    public static void Disable()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        key?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    /// <summary>
    /// 把旧版本 / 安装器留下的「裸路径」自启项原地补上隐藏启动参数，让升级上来的用户不必重新开关一次开关。
    /// 只在自启项本来就指向当前 exe 时才改写（见 <see cref="AutoStartCommand.PointsAt"/>）；返回是否改写过。
    /// </summary>
    public static bool EnsureUpToDate()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return false;

        string? stored = Read();
        if (stored is null || AutoStartCommand.IsCurrent(stored, exePath) || !AutoStartCommand.PointsAt(stored, exePath))
            return false;

        Write();
        return true;
    }

    private static string? Read()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(ValueName) as string;
    }

    private static void Write()
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        key.SetValue(ValueName, AutoStartCommand.Build(Environment.ProcessPath));
    }
}
