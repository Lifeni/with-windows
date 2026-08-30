using WithWindows.Config;
using WithWindows.Core;

namespace WithWindows.Notepad;

/// <summary>
/// 记事本窗口生命周期管理：热键/菜单切换显示与隐藏。
/// 显示中再次触发 = 复制内容到剪贴板并隐藏（原版行为）；关闭窗口同样复制。
/// </summary>
public sealed class NotepadHost
{
    private readonly Logger _log;
    private readonly NotepadWindow _window;

    /// <summary>预创建记事本窗口（隐藏），首次打开无创建黑框；关闭 = 最小化到托盘，窗口常驻。</summary>
    public NotepadHost(string dataRoot, Logger log, ConfigStore configStore)
    {
        _log = log;
        _window = new NotepadWindow(Path.Combine(dataRoot, "notepad.txt"), log, configStore);
    }

    /// <summary>打开并聚焦记事本（托盘左键；已打开则不重复操作）。</summary>
    public void ShowOrFocus() => _window.ShowAndFocus();

    public ActionResult Toggle()
    {
        if (_window.Visible)
        {
            _window.CopyAndHide();
            _log.Info("[notepad] 已复制内容并关闭记事本");
            return new ActionResult(true, "已复制内容并关闭记事本", Notify: false);
        }

        _window.ShowAndFocus();
        _log.Info("[notepad] 已打开记事本");
        return new ActionResult(true, "已打开记事本", Notify: false);
    }
}
