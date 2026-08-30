namespace WithWindows.Core;

/// <summary>
/// 动作执行结果。<see cref="Changed"/> 为 false 表示请求的目标状态与当前一致，没有实际变化——
/// 宿主只记录日志、不弹通知。<see cref="Notify"/> 为 false 表示有变化但不弹通知（如记事本切换）。
/// </summary>
public readonly record struct ActionResult(bool Changed, string Message, bool Notify = true);
