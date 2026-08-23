using WithWindows.Core;
using WithWindows.Notepad;

namespace WithWindows.Actions;

/// <summary>
/// 记事本动作：配置 `{ "hotkey": "F15", "action": "notepad" }`。
/// 切换语义：隐藏 → 显示；可见 → 复制内容到剪贴板并隐藏。
/// </summary>
public sealed class NotepadAction : IAction
{
    private readonly NotepadHost _host;

    public NotepadAction(NotepadHost host) => _host = host;

    public string Name => "notepad";

    public ActionResult Execute(object? args) => _host.Toggle();
}
