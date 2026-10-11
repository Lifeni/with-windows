using Windows.Win32;
using Windows.Win32.Foundation;

namespace WithWindows.Core.Interop;

/// <summary>窗口所在显示器的 DPI 缩放率（DIP → 物理像素的比例）。</summary>
public static class DisplayScale
{
    /// <summary>
    /// 取窗口的缩放率（96 DPI = 1.0）。用 GetDpiForWindow 而不是 WinUI 的 XamlRoot.RasterizationScale：
    /// 后者在窗口首次激活、内容尚未完成布局前会返回 1.0，导致按 100% 误算窗口尺寸。
    /// </summary>
    public static double ForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return 1.0;

        uint dpi = PInvoke.GetDpiForWindow(new HWND(hwnd));
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }
}
