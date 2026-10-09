namespace WithWindows.Core.Actions;

/// <summary>可由热键或界面触发的动作。参数语义由实现定义（例如投屏的 mode）。</summary>
public interface IAction
{
    /// <summary>动作标识，与配置 tools[].id 对应。</summary>
    string Id { get; }

    /// <summary>执行动作；目标状态与当前一致时返回 Changed=false。失败抛异常，由宿主捕获。</summary>
    ActionResult Execute(string argument);
}
