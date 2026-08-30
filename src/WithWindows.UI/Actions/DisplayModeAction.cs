using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Actions;

/// <summary>
/// 切换投影模式，基于 SetDisplayConfig 直接应用拓扑（比 DisplaySwitch.exe 可靠：同步、可验证结果）。
/// mode 取值：internal（仅当前屏幕） / extend（扩展） / external（仅外接） / clone（复制），
/// 以及 "toggle"：在构造注入的候选模式（配置 displayMode.modes，默认 internal/extend）中循环切换。
/// 目标模式与当前一致时返回 Changed=false，宿主不弹通知。
/// </summary>
public sealed class DisplayModeAction
{
    private readonly string[] _toggleModes;

    /// <summary>构造时注入 toggle 候选模式（来自配置 displayMode.modes；空数组 = 默认 internal/extend）。</summary>
    public DisplayModeAction(string[]? toggleModes = null) => _toggleModes = toggleModes ?? Array.Empty<string>();

    /// <summary>执行切换；mode 为 internal / extend / external / clone / toggle（大小写不敏感）。非法值抛 ArgumentException。</summary>
    public ActionResult Execute(string mode)
    {
        string requested = mode.Trim().ToLowerInvariant();
        if (requested is not ("internal" or "extend" or "external" or "clone" or "toggle"))
            throw new ArgumentException($"未知显示模式“{requested}”（支持 internal|extend|external|clone|toggle）");

        var (target, isChange) = Decide(DisplayTopology.GetCurrentMode(), requested, _toggleModes);

        if (!isChange)
            return new ActionResult(false, $"当前已是{ModeDisplayName(target)}");

        Apply(target);
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
        if (current is null)
            return modes[0];
        int index = Array.IndexOf(modes, current);
        return index >= 0 ? modes[(index + 1) % modes.Length] : modes[0];
    }

    internal static void Apply(string mode)
    {
        uint topology = TopologyFor(mode);
        int hr = NativeMethods.SetDisplayConfig(0, IntPtr.Zero, 0, IntPtr.Zero, topology | NativeMethods.SDC_APPLY);
        if (hr != 0)
            throw new InvalidOperationException($"SetDisplayConfig 失败（Win32 错误 {hr}）");
    }

    internal static uint TopologyFor(string mode) => mode switch
    {
        "internal" => NativeMethods.SDC_TOPOLOGY_INTERNAL,
        "extend" => NativeMethods.SDC_TOPOLOGY_EXTEND,
        "external" => NativeMethods.SDC_TOPOLOGY_EXTERNAL,
        "clone" => NativeMethods.SDC_TOPOLOGY_CLONE,
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
