using System;
using System.Collections.Generic;

namespace WithWindows.Core.Config;

/// <summary>显示模式的稳定标识、完整显示名与托盘四字文案。</summary>
public sealed record DisplayModeInfo(string Id, string DisplayName, string MenuName);

/// <summary>显示模式元数据的唯一来源：固定顺序、界面名称与托盘名称。</summary>
public static class DisplayModes
{
    public const string Internal = "internal";
    public const string Extend = "extend";
    public const string External = "external";
    public const string Clone = "clone";

    private static readonly DisplayModeInfo[] Definitions =
    {
        new(Internal, "仅当前屏幕", "当前屏幕"),
        new(Extend, "扩展模式", "扩展模式"),
        new(External, "仅外接屏幕", "外接屏幕"),
        new(Clone, "复制模式", "复制模式"),
    };

    /// <summary>界面显示与补足不足项时使用的固定顺序；切换循环仍以配置中的顺序为准。</summary>
    public static IReadOnlyList<DisplayModeInfo> All { get; } = Array.AsReadOnly(Definitions);

    public static bool IsKnown(string? id)
        => id is Internal or Extend or External or Clone;

    public static string DisplayName(string? id)
        => Find(id)?.DisplayName ?? id ?? "";

    public static string MenuName(string? id)
        => Find(id)?.MenuName ?? id ?? "";

    private static DisplayModeInfo? Find(string? id)
    {
        foreach (DisplayModeInfo mode in Definitions)
        {
            if (string.Equals(mode.Id, id, StringComparison.Ordinal))
                return mode;
        }

        return null;
    }
}
