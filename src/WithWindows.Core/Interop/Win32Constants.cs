namespace WithWindows.Core.Interop;

/// <summary>本项目自用的 Win32 常量（CsWin32 只生成 API 与结构体，不覆盖这类字面量）。</summary>
internal static class Win32Constants
{
    /// <summary>热键消息。</summary>
    internal const uint WM_HOTKEY = 0x0312;
}
