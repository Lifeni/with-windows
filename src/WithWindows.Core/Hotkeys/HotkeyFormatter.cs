namespace WithWindows.Core.Hotkeys;

/// <summary>热键 → 可读字符串（与 <see cref="HotkeyParser"/> 对称）。供录制控件与设置展示使用。</summary>
public static class HotkeyFormatter
{
    /// <summary>格式化为规范顺序的字符串，如 "Ctrl+Alt+Shift+Win+A"。</summary>
    public static string Format(Hotkey hotkey)
    {
        List<string> parts = new(5);
        HotkeyModifiers mods = hotkey.Modifiers;
        if (mods.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (mods.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (mods.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (mods.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
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
