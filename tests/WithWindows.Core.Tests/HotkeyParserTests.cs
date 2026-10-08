using WithWindows.Core.Hotkeys;

namespace WithWindows.Core.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("F13", 0x7C)]
    [InlineData("F14", 0x7D)]
    [InlineData("F24", 0x87)]
    public void Parse_功能键_映射到虚拟键码(string text, uint expectedVk)
    {
        Hotkey hotkey = HotkeyParser.Parse(text);

        Assert.Equal(expectedVk, hotkey.VirtualKey);
        Assert.Equal(HotkeyModifiers.None, hotkey.Modifiers);
    }

    [Theory]
    [InlineData("Ctrl+a", 0x41)]
    [InlineData("Alt+Z", 0x5A)]
    [InlineData("Ctrl+Shift+0", 0x30)]
    [InlineData("Win+9", 0x39)]
    public void Parse_带修饰键的字母数字_映射为大写虚拟键码(string text, uint expectedVk)
    {
        Hotkey hotkey = HotkeyParser.Parse(text);

        Assert.Equal(expectedVk, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("a")]
    [InlineData("Z")]
    [InlineData("0")]
    [InlineData("9")]
    public void TryParse_裸字母或数字_被拒绝(string text)
    {
        // 单个字母/数字无修饰键会全局独占该键，必须拒绝
        bool ok = HotkeyParser.TryParse(text, out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("F13")]
    [InlineData("F24")]
    public void TryParse_裸功能键_允许(string text)
    {
        Assert.True(HotkeyParser.TryParse(text, out _, out _));
    }

    [Theory]
    [InlineData("Ctrl+Shift+F14", HotkeyModifiers.Control | HotkeyModifiers.Shift)]
    [InlineData("alt+f13", HotkeyModifiers.Alt)]
    [InlineData("Win+F13", HotkeyModifiers.Windows)]
    [InlineData("CTRL+ALT+A", HotkeyModifiers.Control | HotkeyModifiers.Alt)]
    public void Parse_修饰键_大小写不敏感且可组合(string text, HotkeyModifiers expectedMods)
    {
        Hotkey hotkey = HotkeyParser.Parse(text);

        Assert.Equal(expectedMods, hotkey.Modifiers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("F25")]
    [InlineData("Ctrl+")]
    [InlineData("+F13")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl+Alt+Delete")]
    [InlineData("Meta+F13")]
    [InlineData("F1+F2")]
    [InlineData(" ")]
    [InlineData(null)]
    public void TryParse_非法输入_失败(string? text)
    {
        bool ok = HotkeyParser.TryParse(text, out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parse_非法输入_抛_FormatException()
    {
        Assert.Throws<FormatException>(() => HotkeyParser.Parse("F25"));
    }

    [Fact]
    public void Parse_重复修饰键_归并为单个位()
    {
        Hotkey hotkey = HotkeyParser.Parse("Ctrl+Ctrl+F13");

        Assert.Equal(HotkeyModifiers.Control, hotkey.Modifiers);
    }

    [Theory]
    [InlineData(0x70u, true)]
    [InlineData(0x87u, true)]
    [InlineData(0x6Fu, false)]
    [InlineData(0x88u, false)]
    [InlineData(0x41u, false)]
    public void IsFunctionKey_边界正确(uint vk, bool expected)
    {
        Assert.Equal(expected, HotkeyParser.IsFunctionKey(vk));
    }
}
