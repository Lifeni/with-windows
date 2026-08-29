using WithWindows.Core;

namespace WithWindows.Tests;

public class HotkeyParserTests
{
    [Theory]
    [InlineData("F1", 0x70)]
    [InlineData("F12", 0x7B)]
    [InlineData("F13", 0x7C)]
    [InlineData("F14", 0x7D)]
    [InlineData("F24", 0x87)]
    public void Parse_FunctionKeys_MapsToVk(string text, uint expectedVk)
    {
        var hotkey = HotkeyParser.Parse(text);

        Assert.Equal(expectedVk, hotkey.VirtualKey);
        Assert.Equal(NativeMethodsForTest.MOD_NOREPEAT, hotkey.Modifiers);
    }

    [Theory]
    [InlineData("a", 0x41)]
    [InlineData("Z", 0x5A)]
    [InlineData("0", 0x30)]
    [InlineData("9", 0x39)]
    public void Parse_SingleCharKeys_MapToUppercaseVk(string text, uint expectedVk)
    {
        var hotkey = HotkeyParser.Parse(text);

        Assert.Equal(expectedVk, hotkey.VirtualKey);
    }

    [Theory]
    [InlineData("Ctrl+Shift+F14", NativeMethodsForTest.MOD_CONTROL | NativeMethodsForTest.MOD_SHIFT)]
    [InlineData("alt+f13", NativeMethodsForTest.MOD_ALT)]
    [InlineData("Win+F13", NativeMethodsForTest.MOD_WIN)]
    [InlineData("CTRL+ALT+A", NativeMethodsForTest.MOD_CONTROL | NativeMethodsForTest.MOD_ALT)]
    public void Parse_Modifiers_AreCaseInsensitiveAndCombined(string text, uint expectedMods)
    {
        var hotkey = HotkeyParser.Parse(text);

        Assert.Equal(expectedMods | NativeMethodsForTest.MOD_NOREPEAT, hotkey.Modifiers);
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
    public void TryParse_InvalidText_Fails(string text)
    {
        bool ok = HotkeyParser.TryParse(text, out _, out string? error);

        Assert.False(ok);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void Parse_Null_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => HotkeyParser.Parse("F25"));
    }
}

internal static class NativeMethodsForTest
{
    public const uint MOD_ALT = 0x0001;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT = 0x0004;
    public const uint MOD_WIN = 0x0008;
    public const uint MOD_NOREPEAT = 0x4000;
}
