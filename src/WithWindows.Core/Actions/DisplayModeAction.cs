using WithWindows.Core.Interop;
using Windows.Win32.Devices.Display;

namespace WithWindows.Core.Actions;

/// <summary>
/// 切换投影模式，基于 SetDisplayConfig 直接应用拓扑。mode 取值：internal / extend / external / clone，
/// 以及 toggle：在候选模式（配置 displayMode.modes，默认 internal/extend）中循环。
/// 目标模式与当前一致时返回 Changed=false。
/// </summary>
public sealed class DisplayModeAction : IAction
{
    private readonly string[] _toggleModes;

    /// <summary>构造时注入 toggle 候选模式（来自配置 displayMode.modes；空数组 = 默认 internal/extend）。</summary>
    public DisplayModeAction(string[]? toggleModes = null) => _toggleModes = toggleModes ?? Array.Empty<string>();

    /// <summary>动作标识，与配置 tools[].id 一致。</summary>
    public string Id => "display_mode";

    /// <summary>执行切换；mode 为 internal / extend / external / clone / toggle（大小写不敏感）。非法值抛 ArgumentException。</summary>
    public ActionResult Execute(string mode)
    {
        string requested = mode.Trim().ToLowerInvariant();
        if (requested is not ("internal" or "extend" or "external" or "clone" or "toggle"))
            throw new ArgumentException($"未知显示模式“{requested}”（支持 internal|extend|external|clone|toggle）");

        var (target, isChange) = Decide(DisplayTopology.GetCurrentMode(), requested, _toggleModes);
        if (!isChange)
            return new ActionResult(false, $"当前已是{ModeDisplayName(target)}");

        DisplayConfigInterop.ApplyTopology((SET_DISPLAY_CONFIG_FLAGS)TopologyFor(target));
        return new ActionResult(true, $"已切换显示模式：{ModeDisplayName(target)}");
    }

    /// <summary>决定目标模式与是否真正变化。纯函数，便于测试。</summary>
    internal static (string Target, bool IsChange) Decide(string? current, string requested, string[] toggleModes)
    {
        string target = requested == "toggle" ? PickToggleTarget(current, toggleModes) : requested;
        // 当前拓扑无法判定（null）时保守执行切换，不阻塞用户操作
        return (target, current is null || target != current);
    }

    /// <summary>toggle 目标选择：当前模式在候选列表中取下一个（循环），否则取第一个。</summary>
    internal static string PickToggleTarget(string? current, string[] toggleModes)
    {
        string[] modes = toggleModes.Length > 0 ? toggleModes : new[] { "internal", "extend" };
        if (current is null) return modes[0];
        int index = Array.IndexOf(modes, current);
        return index >= 0 ? modes[(index + 1) % modes.Length] : modes[0];
    }

    /// <summary>模式名 → SDC_TOPOLOGY_* 标志数值。供测试断言。</summary>
    internal static uint TopologyFor(string mode) => mode switch
    {
        "internal" => 0x00000001,
        "clone" => 0x00000002,
        "extend" => 0x00000004,
        "external" => 0x00000008,
        _ => throw new ArgumentException($"未知显示模式“{mode}”（支持 internal|extend|external|clone）"),
    };

    private static string ModeDisplayName(string mode) => mode switch
    {
        "internal" => "仅当前屏幕",
        "extend" => "扩展模式",
        "external" => "仅外接屏幕",
        _ => "复制模式",
    };
}
