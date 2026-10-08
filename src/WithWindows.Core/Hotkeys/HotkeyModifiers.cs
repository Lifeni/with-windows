namespace WithWindows.Core.Hotkeys;

/// <summary>
/// 热键修饰键。
/// 刻意不使用 Win32 的 MOD_* 数值——核心层不依赖互操作，映射在注册边界完成。
/// </summary>
[Flags]
public enum HotkeyModifiers
{
    /// <summary>无修饰键。仅 F1–F24 允许。</summary>
    None = 0,

    /// <summary>Ctrl。</summary>
    Control = 1 << 0,

    /// <summary>Alt。</summary>
    Alt = 1 << 1,

    /// <summary>Shift。</summary>
    Shift = 1 << 2,

    /// <summary>Win 键。</summary>
    Windows = 1 << 3,
}
