using WithWindows.Interop;

namespace WithWindows.Core;

/// <summary>热键 → 可读字符串（与 <see cref="HotkeyParser"/> 对称）。供录制控件与设置展示使用。</summary>
public static class HotkeyFormatter
{
    public static string Format(Hotkey hotkey)
    {
        var parts = new List<string>(5);
        uint mods = hotkey.Modifiers;
        if ((mods & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
        if ((mods & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
        if ((mods & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
        if ((mods & NativeMethods.MOD_WIN) != 0) parts.Add("Win");
        parts.Add(KeyName(hotkey.VirtualKey));
        return string.Join("+", parts);
    }

    /// <summary>虚拟键码 → 名称（F1–F24、字母、数字；其余返回十六进制）。</summary>
    public static string KeyName(uint vk)
    {
        if (vk is >= 0x70 and <= 0x7B) return $"F{vk - 0x70 + 1}";      // F1–F12
        if (vk is >= 0x7C and <= 0x87) return $"F{vk - 0x7C + 13}";     // F13–F24
        if (vk is >= (uint)'A' and <= (uint)'Z') return ((char)vk).ToString();
        if (vk is >= (uint)'0' and <= (uint)'9') return ((char)vk).ToString();
        return $"0x{vk:X}";
    }
}
