namespace WithWindows.Core;

/// <summary>热键定义：修饰键位 + 虚拟键码。由 <see cref="HotkeyParser"/> 从配置字符串解析。</summary>
public readonly record struct Hotkey(uint Modifiers, uint VirtualKey);
