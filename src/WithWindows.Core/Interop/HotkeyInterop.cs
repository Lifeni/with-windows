using Windows.Win32.UI.Input.KeyboardAndMouse;
using WithWindows.Core.Hotkeys;

namespace WithWindows.Core.Interop;

/// <summary>核心层修饰键枚举 → Win32 注册标志的映射，以及注册错误码的中文说明。</summary>
internal static class HotkeyInterop
{
    /// <summary>Win32 错误码：热键已被其他程序占用。</summary>
    internal const int ErrorHotkeyAlreadyRegistered = 1409;

    /// <summary>Win32 错误码：窗口类已注册（重复注册同名类时出现，可容忍）。</summary>
    internal const int ErrorClassAlreadyExists = 1410;

    /// <summary>
    /// 映射为 RegisterHotKey 的标志位。始终附加 MOD_NOREPEAT：
    /// 「按住不重复触发」是注册层的固定策略，不暴露给上层配置。
    /// </summary>
    internal static HOT_KEY_MODIFIERS ToNativeModifiers(HotkeyModifiers modifiers)
    {
        HOT_KEY_MODIFIERS result = 0;
        if (modifiers.HasFlag(HotkeyModifiers.Control)) result |= HOT_KEY_MODIFIERS.MOD_CONTROL;
        if (modifiers.HasFlag(HotkeyModifiers.Alt)) result |= HOT_KEY_MODIFIERS.MOD_ALT;
        if (modifiers.HasFlag(HotkeyModifiers.Shift)) result |= HOT_KEY_MODIFIERS.MOD_SHIFT;
        if (modifiers.HasFlag(HotkeyModifiers.Windows)) result |= HOT_KEY_MODIFIERS.MOD_WIN;
        return result | HOT_KEY_MODIFIERS.MOD_NOREPEAT;
    }

    /// <summary>注册错误码 → 中文原因。未知错误码保留原始编号，便于排查。</summary>
    internal static string DescribeRegistrationError(int win32Error) => win32Error switch
    {
        ErrorHotkeyAlreadyRegistered => "热键已被其他程序占用",
        0 => "注册失败：系统未返回错误码",
        _ => $"注册失败（Win32 错误 {win32Error}）",
    };
}
