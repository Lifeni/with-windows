using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace WithWindows.Core.Interop;

/// <summary>通过现有托盘图标显示 Windows 原生通知横幅。</summary>
public sealed class TrayNotificationService
{
    private const NOTIFY_ICON_DATA_FLAGS NifInfo = NOTIFY_ICON_DATA_FLAGS.NIF_INFO;
    private const NOTIFY_ICON_INFOTIP_FLAGS NiifInfo = NOTIFY_ICON_INFOTIP_FLAGS.NIIF_INFO;
    private const NOTIFY_ICON_INFOTIP_FLAGS NiifRespectQuietTime =
        NOTIFY_ICON_INFOTIP_FLAGS.NIIF_RESPECT_QUIET_TIME;

    private readonly uint _iconId;
    private readonly WNDENUMPROC _enumWindowCallback;
    private HWND _foundWindow;

    public TrayNotificationService(uint iconId)
    {
        _iconId = iconId;
        _enumWindowCallback = FindIconWindow;
    }

    public bool Show(string title, string message)
    {
        if (FindIconHandle() is not { IsNull: false } iconHandle)
            return false;

        var data = new NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
            hWnd = iconHandle,
            uID = _iconId,
            uFlags = NifInfo,
            dwInfoFlags = NiifInfo | NiifRespectQuietTime,
        };
        for (int index = 0; index < Math.Min(title.Length, 63); index++)
            data.szInfoTitle[index] = title[index];
        for (int index = 0; index < Math.Min(message.Length, 255); index++)
            data.szInfo[index] = message[index];
        return PInvoke.Shell_NotifyIcon(NOTIFY_ICON_MESSAGE.NIM_MODIFY, in data);
    }

    private HWND? FindIconHandle()
    {
        _foundWindow = HWND.Null;
        PInvoke.EnumWindows(_enumWindowCallback, lParam: 0);
        return _foundWindow.IsNull ? null : _foundWindow;
    }

    private BOOL FindIconWindow(HWND window, LPARAM context)
    {
        PInvoke.GetWindowThreadProcessId(window, out uint processId);
        if (processId != (uint)Environment.ProcessId)
            return true;

        var identifier = new NOTIFYICONIDENTIFIER
        {
            cbSize = (uint)Marshal.SizeOf<NOTIFYICONIDENTIFIER>(),
            hWnd = window,
            uID = _iconId,
        };
        if (PInvoke.Shell_NotifyIconGetRect(in identifier, out _).Succeeded)
        {
            _foundWindow = window;
            return false;
        }

        return true;
    }
}
