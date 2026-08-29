using WithWindows.Interop;

namespace WithWindows.Core;

/// <summary>
/// 把配置里的热键字符串解析为 <see cref="Hotkey"/>。
/// 支持：F1–F24、字母、数字；修饰键 Ctrl/Alt/Shift/Win（大小写不敏感），如 "Ctrl+Shift+F14"、"F13"。
/// </summary>
public static class HotkeyParser
{
    private static readonly (string Name, uint Flag)[] KnownModifiers =
    {
        ("ctrl", NativeMethods.MOD_CONTROL),
        ("alt", NativeMethods.MOD_ALT),
        ("shift", NativeMethods.MOD_SHIFT),
        ("win", NativeMethods.MOD_WIN),
    };

    public static bool TryParse(string text, out Hotkey hotkey, out string? error)
    {
        hotkey = default;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "热键为空";
            return false;
        }

        if (text.StartsWith("+") || text.EndsWith("+") || text.Contains("++"))
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

        uint modifiers = 0;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            string part = parts[i].ToLowerInvariant();
            bool found = false;
            foreach (var (name, flag) in KnownModifiers)
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

        if (!TryParseKey(parts[parts.Length - 1], out uint vk))
        {
            error = $"不支持的键“{parts[parts.Length - 1]}”（支持 F1–F24、字母、数字）";
            return false;
        }

        // MOD_NOREPEAT：按住不重复触发，适合"切换类"动作。
        hotkey = new Hotkey(modifiers | NativeMethods.MOD_NOREPEAT, vk);
        return true;
    }

    public static Hotkey Parse(string text)
        => TryParse(text, out Hotkey hotkey, out string? error)
            ? hotkey
            : throw new FormatException(error);

    private static bool TryParseKey(string text, out uint vk)
    {
        vk = 0;

        if (text.Length >= 2 && (text[0] == 'F' || text[0] == 'f')
            && int.TryParse(text.Substring(1), out int fn) && fn is >= 1 and <= 24)
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
