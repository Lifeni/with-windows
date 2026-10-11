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

    // 防 GC：窗口过程委托必须存活于窗口生命周期（实例字段随 MessageWindow 存活）
    private readonly WNDPROC _proc;
    private readonly Action<IntPtr> _onHotkey;
    private HWND _hwnd;

    internal unsafe MessageWindow(Action<IntPtr> onHotkey)
    {
        _onHotkey = onHotkey;
        _proc = WndProc;

        HMODULE module = PInvoke.GetModuleHandle(default(PCWSTR));
        HINSTANCE instance = (HINSTANCE)module.Value;

        fixed (char* className = ClassName)
        {
            PCWSTR name = className;
            var wndClass = new WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<WNDCLASSEXW>(),
                lpfnWndProc = _proc,
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
    }

    internal IntPtr Handle => _hwnd;

    private LRESULT WndProc(HWND hWnd, uint msg, WPARAM wParam, LPARAM lParam)
    {
        if (msg == Win32Constants.WM_HOTKEY)
            _onHotkey((IntPtr)wParam.Value);
        return PInvoke.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    public void Dispose()
    {
        if (!_hwnd.IsNull)
        {
            PInvoke.DestroyWindow(_hwnd);
            _hwnd = HWND.Null;
        }
    }
}
