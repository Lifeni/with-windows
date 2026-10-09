using WithWindows.Core.Hotkeys;
using WithWindows.Core.Interop;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace WithWindows.Core.Tests;

public class HotkeyInteropTests
{
    [Fact]
    public void ToNativeModifiers_无修饰键_仍附加_NOREPEAT()
        => Assert.Equal(HOT_KEY_MODIFIERS.MOD_NOREPEAT, HotkeyInterop.ToNativeModifiers(HotkeyModifiers.None));

    [Fact]
    public void ToNativeModifiers_四键全开_映射为对应位()
    {
        HOT_KEY_MODIFIERS expected = HOT_KEY_MODIFIERS.MOD_CONTROL
            | HOT_KEY_MODIFIERS.MOD_ALT
            | HOT_KEY_MODIFIERS.MOD_SHIFT
            | HOT_KEY_MODIFIERS.MOD_WIN
            | HOT_KEY_MODIFIERS.MOD_NOREPEAT;

        HotkeyModifiers all = HotkeyModifiers.Control | HotkeyModifiers.Alt
            | HotkeyModifiers.Shift | HotkeyModifiers.Windows;

        Assert.Equal(expected, HotkeyInterop.ToNativeModifiers(all));
    }

    [Fact]
    public void DescribeRegistrationError_已占用_给出中文原因()
        => Assert.Equal("热键已被其他程序占用", HotkeyInterop.DescribeRegistrationError(1409));

    [Fact]
    public void DescribeRegistrationError_未知码_保留编号()
        => Assert.Contains("87", HotkeyInterop.DescribeRegistrationError(87));
}
