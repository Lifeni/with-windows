using WithWindows.Interop;

namespace WithWindows.Actions;

/// <summary>
/// 只读查询当前显示拓扑（不修改显示状态）。
/// 使用 QDC_DATABASE_CURRENT + currentTopologyId 输出，由系统给出权威拓扑分类
/// （DISPLAYCONFIG_TOPOLOGY_ID），不依赖路径启发式判定。
/// </summary>
public static class DisplayTopology
{
    // DISPLAYCONFIG_TOPOLOGY_ID
    private const uint TopologyInternal = 1;
    private const uint TopologyClone = 2;
    private const uint TopologyExtend = 4;
    private const uint TopologyExternal = 8;

    /// <summary>返回当前拓扑名："internal" | "clone" | "extend" | "external"；无法判定时返回 null。</summary>
    public static string? GetCurrentMode()
    {
        try
        {
            uint numPath = 0, numMode = 0;
            int sizeRet = NativeMethods.GetDisplayConfigBufferSizes(
                NativeMethods.QDC_DATABASE_CURRENT, ref numPath, ref numMode);
            if (sizeRet != NativeMethods.ERROR_SUCCESS)
                return null;
            if (numPath == 0)
                return null;

            var paths = new DisplayConfigPathInfo[numPath];
            var modes = new DisplayConfigModeInfo[numMode];
            uint topologyId = 0;
            int ret = NativeMethods.QueryDisplayConfig(
                NativeMethods.QDC_DATABASE_CURRENT, ref numPath, paths, ref numMode, modes, out topologyId);
            if (ret != NativeMethods.ERROR_SUCCESS)
                return null;

            return TopologyName(topologyId);
        }
        catch
        {
            // 查询失败时返回 null，调用方按"无法比较"保守处理
            return null;
        }
    }

    /// <summary>拓扑 ID → 模式名；未知值返回 null。纯函数，便于测试。</summary>
    internal static string? TopologyName(uint topologyId) => topologyId switch
    {
        TopologyInternal => "internal",
        TopologyClone => "clone",
        TopologyExtend => "extend",
        TopologyExternal => "external",
        _ => null,
    };
}
