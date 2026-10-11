using WithWindows.Core.Interop;

namespace WithWindows.Core.Actions;

/// <summary>
/// 只读查询当前显示拓扑（不修改显示状态）。
/// 用 QDC_DATABASE_CURRENT 返回的权威 DISPLAYCONFIG_TOPOLOGY_ID 判定，不用路径启发式。
/// </summary>
public static class DisplayTopology
{
    /// <summary>返回当前拓扑名："internal" | "clone" | "extend" | "external"；无法判定时返回 null。</summary>
    public static string? GetCurrentMode()
        => DisplayConfigInterop.QueryCurrentTopology() is uint id ? TopologyName(id) : null;

    /// <summary>拓扑编号 → 模式名；未知值返回 null。纯函数，便于测试。</summary>
    internal static string? TopologyName(uint topologyId) => topologyId switch
    {
        1 => "internal",
        2 => "clone",
        4 => "extend",
        8 => "external",
        _ => null,
    };
}
