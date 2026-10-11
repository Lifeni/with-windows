using WithWindows.Core.Hotkeys;

namespace WithWindows.Core.Tests;

public class HotkeyManagerTests
{
    [Fact]
    public void Register_合法热键_返回成功且无错误()
    {
        using var manager = new HotkeyManager();
        bool ok = manager.Register(new Hotkey(HotkeyModifiers.None, 0x87), () => { }, out string? error);
        Assert.True(ok);
        Assert.Null(error);
    }

    [Fact]
    public void Register_同一热键重复注册_第二次返回占用原因()
    {
        using var manager = new HotkeyManager();
        var hotkey = new Hotkey(HotkeyModifiers.None, 0x87);
        Assert.True(manager.Register(hotkey, () => { }, out _));
        bool second = manager.Register(hotkey, () => { }, out string? error);
        Assert.False(second);
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Fact]
    public void UnregisterAll_之后可重新注册()
    {
        using var manager = new HotkeyManager();
        var hotkey = new Hotkey(HotkeyModifiers.None, 0x87);
        Assert.True(manager.Register(hotkey, () => { }, out _));
        manager.UnregisterAll();
        Assert.True(manager.Register(hotkey, () => { }, out _));
    }
}
