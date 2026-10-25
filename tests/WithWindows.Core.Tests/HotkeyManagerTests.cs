using System.Runtime.CompilerServices;
using WithWindows.Core.Hotkeys;
using WithWindows.Core.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace WithWindows.Core.Tests;

public class HotkeyManagerTests
{
    [Fact]
    public void 多次创建_窗口过程委托在回收后仍有效()
    {
        WeakReference<HotkeyManager> previous = CreateAndDisposeManager();

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(previous.TryGetTarget(out _));

        using var manager = new HotkeyManager();
        Assert.True(manager.Register(
            new Hotkey(HotkeyModifiers.None, 0x87),
            () => { },
            out string? error));
        Assert.Null(error);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference<HotkeyManager> CreateAndDisposeManager()
    {
        var manager = new HotkeyManager();
        var reference = new WeakReference<HotkeyManager>(manager);
        manager.Dispose();
        return reference;
    }

    [Fact]
    public void 多个消息窗口_按句柄路由到各自回调()
    {
        var firstIds = new List<int>();
        var secondIds = new List<int>();
        using var first = new MessageWindow(id => firstIds.Add(id.ToInt32()));
        using var second = new MessageWindow(id => secondIds.Add(id.ToInt32()));

        SendHotkey(first, 7);
        SendHotkey(second, 8);
        first.Dispose();
        SendHotkey(second, 9);

        Assert.Equal([7], firstIds);
        Assert.Equal([8, 9], secondIds);
    }

    [Fact]
    public void Dispose_非创建线程_拒绝静默清理()
    {
        using var window = new MessageWindow(_ => { });
        Exception? captured = null;
        var thread = new Thread(
            () => captured = Record.Exception(window.Dispose));

        thread.Start();
        thread.Join();

        Assert.IsType<InvalidOperationException>(captured);
    }

    private static void SendHotkey(MessageWindow window, int id)
    {
        _ = PInvoke.SendMessage(
            new HWND(window.Handle),
            Win32Constants.WM_HOTKEY,
            new WPARAM((nuint)id),
            default);
    }

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
