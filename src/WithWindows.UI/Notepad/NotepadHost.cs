using WithWindows.Core;

namespace WithWindows.Notepad;

/// <summary>
/// 记事本窗口生命周期管理：热键/菜单切换显示与隐藏。
/// 显示中再次触发 = 复制内容到剪贴板并隐藏（原版行为）；关闭窗口同样复制。
/// </summary>
public sealed class NotepadHost
{
    private readonly string _dataRoot;
    private readonly Logger _log;
    private NotepadWindow? _window;

    public NotepadHost(string dataRoot, Logger log)
    {
        _dataRoot = dataRoot;
        _log = log;
    }

    public ActionResult Toggle()
    {
        if (_window is not null && _window.Visible)
        {
            _window.CopyAndHide();
            _log.Info("[notepad] 已复制内容并关闭记事本");
            return new ActionResult(true, "已复制内容并关闭记事本", Notify: false);
        }

        _window ??= new NotepadWindow(Path.Combine(_dataRoot, "notepad.txt"), _log);
        _window.ShowAndFocus();
        _log.Info("[notepad] 已打开记事本");
        return new ActionResult(true, "已打开记事本", Notify: false);
    }
}
