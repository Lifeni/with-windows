using System.Runtime.InteropServices;
using WithWindows.Interop;

namespace WithWindows.Core;

/// <summary>
/// Win32 窗口尺寸限制（WinUI 3 无 Min/Max 尺寸 API，通过 WM_GETMINMAXINFO 子类化窗口实现）。
/// 单例：仅用于记事本窗口。
/// </summary>
internal static class WindowSizeLimits
{
    private const int GWLP_WNDPROC = -4;
    private const uint WM_GETMINMAXINFO = 0x0024;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT PtReserved;
        public POINT PtMaxSize;
        public POINT PtMaxPosition;
        public POINT PtMinTrackSize;
        public POINT PtMaxTrackSize;
    }

    // 防 GC：原窗口过程与新委托必须存活于窗口生命周期
    private static IntPtr _prevProc;
    private static NativeMethods.WndProcDelegate _proc = null!;

    private static int _minW, _minH;

    /// <summary>给窗口套用最小尺寸限制（不限制最大，可自由放大）。</summary>
    public static void Apply(IntPtr hwnd, int minWidth, int minHeight)
    {
        if (_prevProc != IntPtr.Zero) return; // 已应用（单例）

        _minW = minWidth;
        _minH = minHeight;
        _proc = WndProc;
        _prevProc = NativeMethods.SetWindowLongPtr(hwnd, GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_proc));
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.PtMinTrackSize.X = _minW;
            mmi.PtMinTrackSize.Y = _minH;
            mmi.PtMaxTrackSize.X = 10000; // 不限制最大
            mmi.PtMaxTrackSize.Y = 10000;
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        return NativeMethods.CallWindowProc(_prevProc, hWnd, msg, wParam, lParam);
    }
}
