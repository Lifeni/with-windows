using System.Runtime.InteropServices;
using WithWindows.Interop;

namespace WithWindows.Core;

/// <summary>
/// 全局热键注册与分发。内部持有一个隐藏消息窗口接收 WM_HOTKEY
/// （WinUI 3 无 NativeWindow，改用 CreateWindowEx + 窗口过程），
/// 注册的句柄随窗口销毁由系统自动注销（Dispose 时显式注销）。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private sealed class MessageWindow : IDisposable
    {
        private const string ClassName = "WithWindows.HotkeyWindow";

        // 防 GC：窗口过程委托必须存活于窗口生命周期（实例委托随 MessageWindow 存活）
        private readonly NativeMethods.WndProcDelegate _proc;
        private readonly Action<IntPtr> _onHotkey;
        private IntPtr _hwnd;

        public MessageWindow(Action<IntPtr> onHotkey)
        {
            _onHotkey = onHotkey;
            _proc = WndProc;
            var wndClass = new NativeMethods.WNDCLASSEX
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEX>(),
                lpfnWndProc = _proc,
                lpszClassName = ClassName,
                hInstance = NativeMethods.GetModuleHandle(null),
            };
            NativeMethods.RegisterClassEx(ref wndClass);
            _hwnd = NativeMethods.CreateWindowEx(
                0, ClassName, ClassName, 0, 0, 0, 0, 0,
                IntPtr.Zero, IntPtr.Zero, wndClass.hInstance, IntPtr.Zero);
        }

        public IntPtr Handle => _hwnd;

        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == NativeMethods.WM_HOTKEY)
                _onHotkey(wParam);
            return NativeMethods.DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
            {
                NativeMethods.DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
        }
    }

    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly List<int> _registeredIds = new();
    private int _nextId = 1;

    public HotkeyManager() => _window = new MessageWindow(OnHotkeyMessage);

    /// <summary>注册热键；失败（如被其他程序占用）返回 false 并给出原因，不抛异常。</summary>
    public bool Register(Hotkey hotkey, Action handler, out string? error)
    {
        int id = _nextId++;
        if (!NativeMethods.RegisterHotKey(_window.Handle, id, hotkey.Modifiers, hotkey.VirtualKey))
        {
            int err = Marshal.GetLastWin32Error();
            error = err == NativeMethods.ERROR_HOTKEY_ALREADY_REGISTERED
                ? "热键已被占用"
                : $"注册失败 (Win32 错误 {err})";
            return false;
        }
        _handlers[id] = handler;
        _registeredIds.Add(id);
        error = null;
        return true;
    }

    private void OnHotkeyMessage(IntPtr wParam)
    {
        int id = wParam.ToInt32();
        if (_handlers.TryGetValue(id, out var handler))
            handler();
    }

    public void Dispose()
    {
        foreach (int id in _registeredIds)
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        _registeredIds.Clear();
        _handlers.Clear();
        _window.Dispose();
    }
}
