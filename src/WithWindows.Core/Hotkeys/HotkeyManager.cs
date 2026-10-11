using System.Runtime.InteropServices;
using WithWindows.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace WithWindows.Core.Hotkeys;

/// <summary>
/// 全局热键注册与分发。内部持有独立隐藏消息窗口接收 WM_HOTKEY（绝不子类化 WinUI 窗口）；
/// 注册的句柄随窗口销毁由系统自动注销，<see cref="Dispose"/> 时再显式注销一遍。
/// 注册失败返回错误原因，不抛异常。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private readonly MessageWindow _window;
    private readonly Dictionary<int, Action> _handlers = new();
    private readonly List<int> _registeredIds = new();
    private int _nextId = 1;

    public HotkeyManager() => _window = new MessageWindow(OnHotkeyMessage);

    /// <summary>注册热键；失败返回 false 并给出中文原因（1409 = 已被占用）。</summary>
    public bool Register(Hotkey hotkey, Action handler, out string? error)
    {
        int id = _nextId++;
        bool ok = PInvoke.RegisterHotKey(
            new HWND(_window.Handle),
            id,
            HotkeyInterop.ToNativeModifiers(hotkey.Modifiers),
            hotkey.VirtualKey);

        if (!ok)
        {
            error = HotkeyInterop.DescribeRegistrationError(Marshal.GetLastWin32Error());
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
        if (_handlers.TryGetValue(id, out Action? handler))
            handler();
    }

    /// <summary>注销全部热键（热重载前调用；再次 Register 即重新注册）。</summary>
    public void UnregisterAll()
    {
        foreach (int id in _registeredIds)
            PInvoke.UnregisterHotKey(new HWND(_window.Handle), id);
        _registeredIds.Clear();
        _handlers.Clear();
    }

    public void Dispose()
    {
        UnregisterAll();
        _window.Dispose();
    }
}
