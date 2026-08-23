using WithWindows.Core;

namespace WithWindows.Notepad;

/// <summary>
/// 记事本单例宿主：管理窗口生命周期与切换逻辑。
/// 热键切换：隐藏 → 显示；可见 → 复制内容到剪贴板并隐藏（内容保留，下次唤醒可继续编辑）。
/// </summary>
public sealed class NotepadHost : IDisposable
{
    private readonly string _dataDir;
    private NotepadWindow? _window;

    public NotepadHost(string dataDir) => _dataDir = dataDir;

    public ActionResult Toggle()
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new NotepadWindow(_dataDir);
            _window.ShowWindow();
            return new ActionResult(true, "已打开记事本", Notify: false);
        }

        if (_window.Visible)
        {
            // 快捷键关闭：先把文本复制到剪贴板，再直接关闭窗口
            _window.CopyToClipboard();
            _window.Close();
            return new ActionResult(true, "已复制内容并关闭记事本", Notify: false);
        }

        _window.ShowWindow();
        return new ActionResult(true, "已显示记事本", Notify: false);
    }

    /// <summary>托盘菜单入口：直接显示（不复制不隐藏）。</summary>
    public void Show()
    {
        if (_window is null || _window.IsDisposed)
            _window = new NotepadWindow(_dataDir);
        _window.ShowWindow();
    }

    public void Dispose()
    {
        if (_window is not null && !_window.IsDisposed)
            _window.Close();
        _window = null;
    }
}
