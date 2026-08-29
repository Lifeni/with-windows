namespace WithWindows.Core;

/// <summary>
/// 动作执行结果。<see cref="Changed"/> 为 false 表示请求的目标状态与当前一致，没有实际变化——
/// 宿主只记录日志、不弹通知。<see cref="Notify"/> 为 false 表示有变化但不弹通知（如记事本切换）。
/// </summary>
public readonly record struct ActionResult(bool Changed, string Message, bool Notify = true);

/// <summary>
/// 一键动作接口。新动作：实现此接口并在 Program 启动时注册到 <see cref="ActionRegistry"/>。
/// <see cref="Execute"/> 返回执行结果；失败抛异常由宿主捕获。
/// </summary>
public interface IAction
{
    /// <summary>配置中引用的动作名，如 "display_mode"。</summary>
    string Name { get; }

    /// <summary>执行动作。args 为配置条目中的 args 字段（JSON 值）。</summary>
    ActionResult Execute(object? args);
}
