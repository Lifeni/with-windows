using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Tests;

public class HotkeyFormatterTests
{
    [Fact]
    public void Format_ModifiersInCanonicalOrder()
    {
        var hotkey = new Hotkey(NativeMethods.MOD_SHIFT | NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_WIN, (uint)'A');

        Assert.Equal("Ctrl+Alt+Shift+Win+A", HotkeyFormatter.Format(hotkey));
    }

    [Theory]
    [InlineData(0x70u, "F1")]
    [InlineData(0x7Bu, "F12")]
    [InlineData(0x7Cu, "F13")]
    [InlineData(0x87u, "F24")]
    public void KeyName_FunctionKeys(uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyFormatter.KeyName(vk));
    }

    [Fact]
    public void Format_SingleKey_NoModifiers()
    {
        Assert.Equal("F13", HotkeyFormatter.Format(new Hotkey(0, 0x7C)));
    }

    [Fact]
    public void Format_LetterAndDigit()
    {
        Assert.Equal("D", HotkeyFormatter.Format(new Hotkey(0, (uint)'D')));
        Assert.Equal("5", HotkeyFormatter.Format(new Hotkey(0, (uint)'5')));
    }

    [Fact]
    public void RoundTrip_FormatThenParse()
    {
        var original = HotkeyParser.Parse("Ctrl+Shift+F14");

        var parsed = HotkeyParser.Parse(HotkeyFormatter.Format(original));

        Assert.Equal(original.Modifiers & ~NativeMethods.MOD_NOREPEAT, parsed.Modifiers & ~NativeMethods.MOD_NOREPEAT);
        Assert.Equal(original.VirtualKey, parsed.VirtualKey);
    }
}
