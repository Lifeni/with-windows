namespace WithWindows.Core;

/// <summary>
/// 动作执行结果。<see cref="Changed"/> 为 false 表示请求的目标状态与当前一致，没有实际变化——
/// 宿主只记录日志，不重复提示成功。
/// </summary>
public readonly record struct ActionResult(bool Changed, string Message);
