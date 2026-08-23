using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using WithWindows.Interop;

namespace WithWindows;

/// <summary>
/// 自管系统托盘图标：隐藏消息窗口 + NOTIFYICONDATA。
/// 相比 WinForms NotifyIcon 的优势：气泡通知可用 NIIF_USER 显示自定义图标（闪电），
/// 且不受 ShowBalloonTip 只能使用系统预设图标的限制。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private const uint NIM_ADD = 0;
    private const uint NIM_MODIFY = 1;
    private const uint NIM_DELETE = 2;
    private const uint NIM_SETVERSION = 4;

    private const uint NIF_MESSAGE = 0x0001;
    private const uint NIF_ICON = 0x0002;
    private const uint NIF_TIP = 0x0004;
    private const uint NIF_INFO = 0x0010;

    private const uint NIIF_USER = 0x0004;
    private const uint NOTIFYICON_VERSION_4 = 4;

    private const int WM_NOTIFYICON = 0x0400 + 1;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int WM_RBUTTONUP = 0x0205;

    private sealed class TrayWindow : NativeWindow
    {
        private readonly Action<Message> _handler;

        public TrayWindow(Action<Message> handler)
        {
            _handler = handler;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NOTIFYICON)
                _handler(m);
            base.WndProc(ref m);
        }
    }

    private readonly TrayWindow _window;
    private readonly ModernMenu _menu;
    private readonly Icon _icon;
    private NotifyIconData _data;
    private bool _disposed;

    /// <summary>菜单由调用方构建(动作项由 App 决定),TrayIcon 负责弹出。</summary>
    public TrayIcon(Icon icon, ModernMenu menu)
    {
        _icon = icon;
        _menu = menu;
        _window = new TrayWindow(OnTrayMessage);

        _data = new NotifyIconData
        {
            CbSize = (uint)Marshal.SizeOf<NotifyIconData>(),
            Hwnd = _window.Handle,
            Id = 0,
            Flags = NIF_MESSAGE | NIF_ICON | NIF_TIP,
            CallbackMessage = WM_NOTIFYICON,
            Icon = icon.Handle,
            Tip = "With Windows",
        };
        if (!NativeMethods.Shell_NotifyIcon(NIM_ADD, ref _data))
            throw new InvalidOperationException($"Shell_NotifyIcon（NIM_ADD）失败（Win32 错误 {Marshal.GetLastWin32Error()}）");

        // v4：右键菜单使用 WM_CONTEXTMENU 消息，且支持更大 tip 文本
        _data.TimeoutOrVersion = NOTIFYICON_VERSION_4;
        NativeMethods.Shell_NotifyIcon(NIM_SETVERSION, ref _data);
    }

    /// <summary>弹出气泡通知，图标为应用图标（闪电）。timeoutMs 仅在 Windows 7 及更早系统生效。</summary>
    public void ShowBalloon(string title, string text, int timeoutMs = 3000)
    {
        if (_disposed)
            return;

        var data = _data;
        data.Flags = NIF_INFO | NIF_ICON;
        data.Icon = _icon.Handle;
        data.InfoTitle = title;
        data.Info = text;
        data.InfoFlags = NIIF_USER;
        data.TimeoutOrVersion = (uint)timeoutMs; // NIF_INFO 时该字段为 uTimeout
        NativeMethods.Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void OnTrayMessage(Message m)
    {
        int msg = m.LParam.ToInt32();
        // 只有右键弹出菜单；左键不做任何操作（与 Win11 托盘惯例一致）
        if (msg == WM_CONTEXTMENU || msg == WM_RBUTTONUP)
            ShowMenu();
    }

    /// <summary>弹出托盘菜单（Win11 风格自绘弹窗，自身激活后点击外部自动关闭）。</summary>
    private void ShowMenu()
    {
        _menu.ShowAt(Cursor.Position);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        NativeMethods.Shell_NotifyIcon(NIM_DELETE, ref _data);
        _window.DestroyHandle();
        _menu.Dispose();
    }
}
