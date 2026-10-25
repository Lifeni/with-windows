using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WithWindows.Core.Interop;

/// <summary>
/// 隐藏消息窗口：只用于接收 WM_HOTKEY 等线程消息。
/// 绝不在 WinUI 窗口上做子类化——项目已验证两次会与 WinUI 3 消息泵冲突并原生崩溃（0xc000027b）。
/// </summary>
internal sealed class MessageWindow : IDisposable
{
    private const string ClassName = "WithWindows.HotkeyWindow";

    // 窗口类在进程内只注册一次，因此窗口过程必须与进程同寿命；按 HWND 路由到当前实例。
    private static readonly WNDPROC StaticProc = StaticWndProc;
    private static readonly ConcurrentDictionary<nint, MessageWindow> LiveWindows = [];

    private readonly Action<IntPtr> _onHotkey;
    private readonly int _ownerThreadId;
    private HWND _hwnd;

    internal unsafe MessageWindow(Action<IntPtr> onHotkey)
    {
        _onHotkey = onHotkey;
        _ownerThreadId = Environment.CurrentManagedThreadId;

        HMODULE module = PInvoke.GetModuleHandle(default(PCWSTR));
        HINSTANCE instance = (HINSTANCE)module.Value;

        fixed (char* className = ClassName)
        {
            PCWSTR name = className;
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = StaticProc,
                lpszClassName = name,
                hInstance = instance,
            };

            // 重复注册同名类会返回 0 并置 ERROR_CLASS_ALREADY_EXISTS（1410），可忽略
            PInvoke.RegisterClassEx(wndClass);

            _hwnd = PInvoke.CreateWindowEx(
                WINDOW_EX_STYLE.WS_EX_LEFT,
                name,
                name,
                WINDOW_STYLE.WS_OVERLAPPED,
                0, 0, 0, 0,
                HWND.Null,
                HMENU.Null,
                instance,
                null);
        }

        if (!_hwnd.IsNull)
            LiveWindows[HandleKey(_hwnd)] = this;
    }

    internal IntPtr Handle => _hwnd;

    private static unsafe nint HandleKey(HWND hwnd) => (nint)hwnd.Value;

    private static LRESULT StaticWndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == Win32Constants.WM_HOTKEY
            && LiveWindows.TryGetValue(HandleKey(hWnd), out MessageWindow? window))
        {
            window?._onHotkey((IntPtr)wParam.Value);
        }

        return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (_hwnd.IsNull)
            return;

        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "热键消息窗口只能在创建它的线程上释放。");
        }

        HWND hwnd = _hwnd;
        _hwnd = HWND.Null;
        LiveWindows.TryRemove(HandleKey(hwnd), out _);
        if (!PInvoke.DestroyWindow(hwnd))
        {
            int error = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"销毁热键消息窗口失败（Win32 错误 {error}）。");
        }
    }
}
