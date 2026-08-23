using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Actions;

/// <summary>
/// 切换投影模式，基于 SetDisplayConfig 直接应用拓扑（比 DisplaySwitch.exe 可靠：同步、可验证结果）。
/// args 支持 {"mode": "internal"} 或字符串 "internal"；mode 取值：
/// internal（仅当前屏幕） / extend（扩展） / external（仅外接） / clone（复制），
/// 以及 "toggle": 在 args.modes（默认 ["internal", "extend"]）中自动判断当前模式并切换到下一个。
/// 目标模式与当前一致时返回 Changed=false，宿主不弹通知。
/// </summary>
public sealed class DisplayModeAction : IAction
{
    public string Name => "display_mode";

    public ActionResult Execute(object? args)
    {
        var (requested, toggleModes) = ParseArgs(args);
        var (target, isChange) = Decide(DisplayTopology.GetCurrentMode(), requested, toggleModes);

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

    internal static (string Mode, string[] Modes) ParseArgs(object? args)
    {
        if (args is JsonObject obj)
        {
            string? mode = obj.TryGet("mode", out var modeVal) && modeVal is JsonString modeStr
                ? modeStr.Value.Trim().ToLowerInvariant()
                : null;

            string[] modes = Array.Empty<string>();
            if (obj.TryGet("modes", out var modesVal) && modesVal is JsonArray modesArr)
                modes = modesArr.Items.OfType<JsonString>()
                    .Select(x => x.Value.Trim().ToLowerInvariant())
                    .ToArray();

            if (string.IsNullOrEmpty(mode))
                throw new ArgumentException("缺少 args.mode（internal|extend|external|clone|toggle）");
            return (mode, modes);
        }

        if (args is JsonString text && !string.IsNullOrWhiteSpace(text.Value))
            return (text.Value.Trim().ToLowerInvariant(), Array.Empty<string>());

        if (args is string direct && !string.IsNullOrWhiteSpace(direct))
            return (direct.Trim().ToLowerInvariant(), Array.Empty<string>());

        throw new ArgumentException("缺少 args.mode（internal|extend|external|clone|toggle）");
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
