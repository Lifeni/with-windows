using WithWindows.Core.Hotkeys;

namespace WithWindows.Core.Tests;

public class HotkeyFormatterTests
{
    [Fact]
    public void Format_修饰键按规范顺序输出()
    {
        var hotkey = new Hotkey(
            HotkeyModifiers.Shift | HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Windows,
            (uint)'A');

        Assert.Equal("Ctrl+Alt+Shift+Win+A", HotkeyFormatter.Format(hotkey));
    }

    [Theory]
    [InlineData(0x70u, "F1")]
    [InlineData(0x7Bu, "F12")]
    [InlineData(0x7Cu, "F13")]
    [InlineData(0x87u, "F24")]
    public void KeyName_功能键(uint vk, string expected)
    {
        Assert.Equal(expected, HotkeyFormatter.KeyName(vk));
    }

    [Fact]
    public void Format_无修饰键的单键()
    {
        Assert.Equal("F13", HotkeyFormatter.Format(new Hotkey(HotkeyModifiers.None, 0x7C)));
    }

    [Fact]
    public void Format_字母与数字()
    {
        Assert.Equal("D", HotkeyFormatter.Format(new Hotkey(HotkeyModifiers.None, (uint)'D')));
        Assert.Equal("5", HotkeyFormatter.Format(new Hotkey(HotkeyModifiers.None, (uint)'5')));
    }

    [Fact]
    public void KeyName_未知键码_输出十六进制()
    {
        Assert.Equal("0x2", HotkeyFormatter.KeyName(0x02));
    }

    [Fact]
    public void 往返_格式化后再解析_保持一致()
    {
        Hotkey original = HotkeyParser.Parse("Ctrl+Shift+F14");

        Hotkey parsed = HotkeyParser.Parse(HotkeyFormatter.Format(original));

        Assert.Equal(original, parsed);
    }

    [Theory]
    [InlineData("F1")]
    [InlineData("Alt+F13")]
    [InlineData("Ctrl+Alt+Shift+Win+Z")]
    [InlineData("Win+9")]
    public void 往返_多组输入(string text)
    {
        Hotkey original = HotkeyParser.Parse(text);

        Assert.Equal(original, HotkeyParser.Parse(HotkeyFormatter.Format(original)));
    }
}
