using System.Runtime.InteropServices;
using System.Windows.Forms;
using WithWindows.Interop;

namespace WithWindows.Core;

/// <summary>
/// 全局热键注册与分发。内部持有一个隐藏 NativeWindow 接收 WM_HOTKEY，
/// 注册的句柄随窗口销毁由系统自动注销（Dispose 时显式注销）。
/// </summary>
public sealed class HotkeyManager : IDisposable
{
    private sealed class MessageWindow : NativeWindow
    {
        private readonly Action<Message> _handler;

        public MessageWindow(Action<Message> handler)
        {
            _handler = handler;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
                _handler(m);
            base.WndProc(ref m);
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

    private void OnHotkeyMessage(Message m)
    {
        int id = m.WParam.ToInt32();
        if (_handlers.TryGetValue(id, out var handler))
            handler();
    }

    public void Dispose()
    {
        foreach (int id in _registeredIds)
            NativeMethods.UnregisterHotKey(_window.Handle, id);
        _registeredIds.Clear();
        _handlers.Clear();
        _window.DestroyHandle();
    }
}
