using System.Runtime.InteropServices;

namespace WithWindows.Core;

/// <summary>
/// Win32 窗口最小尺寸限制（WM_GETMINMAXINFO）：拖动时即被系统限制，无回弹无闪烁。
/// 用 SetWindowSubclass（comctl32）链式子类化，不覆盖 WinUI 3 内部窗口过程。
/// 单例：仅用于记事本窗口。
/// </summary>
internal static class WindowSizeLimits
{
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

    private delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData);

    // 防 GC：子类过程委托必须存活
    private static SubclassProc _proc = null!;
    private static int _minW, _minH;

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc pfnSubclass, UIntPtr uIdSubclass, IntPtr dwRefData);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>给窗口套用最小尺寸限制（不限制最大，可自由放大）。</summary>
    public static void Apply(IntPtr hwnd, int minWidth, int minHeight)
    {
        if (_proc is not null) return; // 已应用（单例）

        _minW = minWidth;
        _minH = minHeight;
        _proc = WndProc;
        SetWindowSubclass(hwnd, _proc, (UIntPtr)1, IntPtr.Zero);
    }

    private static IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr uIdSubclass, IntPtr dwRefData)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);
            mmi.PtMinTrackSize.X = _minW;
            mmi.PtMinTrackSize.Y = _minH;
            Marshal.StructureToPtr(mmi, lParam, false);
        }
        return DefSubclassProc(hWnd, msg, wParam, lParam);
    }
}
