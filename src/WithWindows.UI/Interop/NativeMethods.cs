using System.Runtime.InteropServices;

namespace WithWindows.Interop;

internal static class NativeMethods
{
    public const int WM_HOTKEY = 0x0312;

    // RegisterHotKey modifiers
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;

    public const uint ERROR_HOTKEY_ALREADY_REGISTERED = 1409;

    // SetDisplayConfig topology flags
    public const uint SDC_TOPOLOGY_INTERNAL = 0x00000001;
    public const uint SDC_TOPOLOGY_CLONE = 0x00000002;
    public const uint SDC_TOPOLOGY_EXTEND = 0x00000004;
    public const uint SDC_TOPOLOGY_EXTERNAL = 0x00000008;
    public const uint SDC_APPLY = 0x00000080;

    // QueryDisplayConfig flags
    public const uint QDC_DATABASE_CURRENT = 0x00000004;

    // 主题切换广播（WM_SETTINGCHANGE）
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint SMTO_ABORTIFHUNG = 0x0002;
    public static readonly IntPtr HWND_BROADCAST = new(0xFFFF);

    public const uint ERROR_SUCCESS = 0;
    public const uint ERROR_INSUFFICIENT_BUFFER = 122;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetDisplayConfig(
        uint numPathArrayElements,
        IntPtr pathArray,
        uint numModeInfoArrayElements,
        IntPtr modeInfoArray,
        uint flags);

    [DllImport("user32.dll")]
    public static extern int GetDisplayConfigBufferSizes(
        uint flags,
        ref uint numPathArrayElements,
        ref uint numModeInfoArrayElements);

    [DllImport("user32.dll")]
    public static extern int QueryDisplayConfig(
        uint flags,
        ref uint numPathArrayElements,
        [In, Out] DisplayConfigPathInfo[]? pathArray,
        ref uint numModeInfoArrayElements,
        [In, Out] DisplayConfigModeInfo[]? modeInfoArray,
        out uint currentTopologyId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, IntPtr lParam,
        uint fuFlags, uint timeout, out UIntPtr result);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadImage(IntPtr hinst, string lpszName, uint type, int cx, int cy, uint fuLoad);

    // ---- 隐藏消息窗口（热键接收；WinUI 3 无托管消息窗口，手写 CreateWindowEx） ----

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassEx(ref WNDCLASSEX wndClassEx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(
        uint exStyle, string className, string windowName, uint style,
        int x, int y, int width, int height,
        IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern IntPtr CallWindowProc(IntPtr prevWndProc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    public delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public WndProcDelegate lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public IntPtr lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }
}
