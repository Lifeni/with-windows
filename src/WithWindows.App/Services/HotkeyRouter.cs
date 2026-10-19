using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Tools;

namespace WithWindows.Services;

/// <summary>
/// 把工具标识映射到具体行为：有页面的工具按一次显示、再按一次隐藏（并切页），纯动作工具直接执行。
/// 托盘、热键与快捷键页的「立即执行」共用这一处分派。
/// </summary>
public sealed class HotkeyRouter
{
    private readonly MainWindow _window;
    private readonly AppConfig _config;

    public HotkeyRouter(MainWindow window, AppConfig config)
    {
        _window = window;
        _config = config;
    }

    /// <summary>执行工具；页面类工具返回 null，动作类工具返回执行结果。</summary>
    public ActionResult? Invoke(string toolId)
    {
        switch (toolId)
        {
            case ToolCatalog.NotepadId:
                _window.ToggleWindow(ToolCatalog.NotepadId);
                return null;

            case ToolCatalog.DisplayModeId:
                var action = new DisplayModeAction(_config.DisplayMode.Modes.ToArray());
                return action.Execute("toggle");

            default:
                throw new ArgumentException($"未知工具“{toolId}”");
        }
    }
}
