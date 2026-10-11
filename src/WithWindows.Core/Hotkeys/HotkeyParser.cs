namespace WithWindows.Core.Hotkeys;

/// <summary>
/// 把配置里的热键字符串解析为 <see cref="Hotkey"/>。
/// 支持：F1–F24、字母、数字；修饰键 Ctrl/Alt/Shift/Win（大小写不敏感），如 "Ctrl+Shift+F14"、"F13"。
/// </summary>
public static class HotkeyParser
{
    private static readonly (string Name, HotkeyModifiers Flag)[] KnownModifiers =
    [
        ("ctrl", HotkeyModifiers.Control),
        ("alt", HotkeyModifiers.Alt),
        ("shift", HotkeyModifiers.Shift),
        ("win", HotkeyModifiers.Windows),
    ];

    /// <summary>解析热键字符串；失败时返回 false 并给出中文原因，不抛异常。</summary>
    public static bool TryParse(string? text, out Hotkey hotkey, out string? error)
    {
        hotkey = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "热键为空";
            return false;
        }

        if (text.StartsWith('+') || text.EndsWith('+') || text.Contains("++", StringComparison.Ordinal))
        {
            error = "格式错误：应为“[修饰键+]键”，如“Ctrl+Shift+F14”";
            return false;
        }

        string[] parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            error = "热键为空";
            return false;
        }

        HotkeyModifiers modifiers = HotkeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            string part = parts[i].ToLowerInvariant();
            bool found = false;
            foreach ((string name, HotkeyModifiers flag) in KnownModifiers)
            {
                if (part == name)
                {
                    modifiers |= flag;
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                error = $"未知修饰键“{parts[i]}”";
                return false;
            }
        }

        if (!TryParseKey(parts[^1], out uint vk))
        {
            error = $"不支持的键“{parts[^1]}”（支持 F1–F24、字母、数字）";
            return false;
        }

        // 单个字母/数字不带修饰键会全局独占该键，容易误配置（F1–F24 属专用功能键，允许单键）
        if (modifiers == HotkeyModifiers.None && !IsFunctionKey(vk))
        {
            error = "需要至少一个修饰键（Ctrl / Alt / Shift / Win），或使用 F1–F24";
            return false;
        }

        hotkey = new Hotkey(modifiers, vk);
        return true;
    }

    /// <summary>解析热键字符串；失败时抛 <see cref="FormatException"/>。</summary>
    public static Hotkey Parse(string? text)
        => TryParse(text, out Hotkey hotkey, out string? error)
            ? hotkey
            : throw new FormatException(error);

    /// <summary>虚拟键码是否为 F1–F24。</summary>
    public static bool IsFunctionKey(uint vk) => vk is >= 0x70 and <= 0x87;

    private static bool TryParseKey(string text, out uint vk)
    {
        vk = 0;

        if (text.Length >= 2 && (text[0] == 'F' || text[0] == 'f')
            && int.TryParse(text.AsSpan(1), out int fn) && fn is >= 1 and <= 24)
        {
            // F1–F12： 0x70–0x7B；F13–F24： 0x7C–0x87（可编程键盘专用键，不与系统冲突）
            vk = fn <= 12 ? (uint)(0x70 + fn - 1) : (uint)(0x7C + fn - 13);
            return true;
        }

        if (text.Length == 1)
        {
            char c = char.ToUpperInvariant(text[0]);
            if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
            {
                vk = c;
                return true;
            }
        }

        return false;
    }
}
