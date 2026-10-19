using Windows.Win32;
using Windows.Win32.Foundation;

namespace WithWindows.Core.Interop;

/// <summary>
/// 窗口可见性与前台状态的 Win32 查询（供「显示／隐藏窗口」热键判定使用）。
/// 空句柄一律视为「不可见、不在前台」：热键回调里句柄理论上不会为空，
/// 但真为空时若把 0 当成前台窗口"匹配"，会把窗口误判成前台而直接藏起来。
/// </summary>
public static class WindowInterop
{
    public static bool IsVisible(IntPtr hwnd)
        => hwnd != IntPtr.Zero && PInvoke.IsWindowVisible(new HWND(hwnd));

    public static bool IsForeground(IntPtr hwnd)
        => hwnd != IntPtr.Zero && PInvoke.GetForegroundWindow() == new HWND(hwnd);

    /// <summary>
    /// 窗口是否最小化。最小化时 <see cref="IsVisible"/> 仍为 true（用户却看不到内容），
    /// 必须单独判定——否则最小化的窗口会被当成「已显示」而唤不回来。
    /// </summary>
    public static bool IsMinimized(IntPtr hwnd)
        => hwnd != IntPtr.Zero && PInvoke.IsIconic(new HWND(hwnd));

    /// <summary>
    /// 把窗口提到最前。WinUI 的 <c>Window.Activate()</c> 对「已可见但在别的窗口后面」不可靠
    /// （实测：窗口没被提到前台），所以再显式调一次 Win32 兜底。
    /// </summary>
    public static void BringToFront(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        PInvoke.SetForegroundWindow(new HWND(hwnd));
    }
}
