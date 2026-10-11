namespace WithWindows.Core.Hotkeys;

/// <summary>
/// 热键定义：修饰键集合 + 虚拟键码。
/// 「按住不重复触发」是注册时的固定策略，不作为本模型的一部分。
/// </summary>
public readonly record struct Hotkey(HotkeyModifiers Modifiers, uint VirtualKey);
