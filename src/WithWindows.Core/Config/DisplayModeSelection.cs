using System;
using System.Collections.Generic;
using System.Linq;

namespace WithWindows.Core.Config;

/// <summary>显示模式候选的最少项数与选择变更规则。</summary>
public static class DisplayModeSelection
{
    /// <summary>循环切换至少需要两个不同模式，否则切换不会产生变化。</summary>
    public const int MinimumCount = 2;

    /// <summary>
    /// 过滤未知模式、去重，并补足到至少两项。已有两项及以上时保留原循环顺序；
    /// 旧配置不足两项时，缺失项按固定顺序补足。
    /// </summary>
    public static List<string> Normalize(IEnumerable<string>? modes)
    {
        var result = new List<string>();
        var selected = new HashSet<string>(StringComparer.Ordinal);
        if (modes is not null)
        {
            foreach (string mode in modes)
            {
                if (DisplayModes.IsKnown(mode) && selected.Add(mode))
                    result.Add(mode);
            }
        }

        if (result.Count >= MinimumCount)
            return result;

        foreach (DisplayModeInfo mode in DisplayModes.All)
        {
            if (selected.Count >= MinimumCount)
                break;

            selected.Add(mode.Id);
        }

        return DisplayModes.All
            .Where(mode => selected.Contains(mode.Id))
            .Select(mode => mode.Id)
            .ToList();
    }

    /// <summary>当前选择是否已经达到最低项数；此时界面应锁定已选项。</summary>
    public static bool IsAtMinimum(IEnumerable<string>? modes)
        => Normalize(modes).Count <= MinimumCount;

    /// <summary>
    /// 应用一次复选框变更。取消后少于两项时返回 false，<paramref name="next"/> 保持原选择。
    /// </summary>
    public static bool TryChangeSelection(
        IReadOnlyList<string>? modes,
        string mode,
        bool isChecked,
        out List<string> next)
    {
        next = Normalize(modes);
        if (!DisplayModes.IsKnown(mode))
            return false;

        var selected = new HashSet<string>(next, StringComparer.Ordinal);
        if (isChecked)
        {
            selected.Add(mode);
        }
        else if (selected.Contains(mode))
        {
            if (selected.Count <= MinimumCount)
                return false;

            selected.Remove(mode);
        }

        next = DisplayModes.All
            .Where(info => selected.Contains(info.Id))
            .Select(info => info.Id)
            .ToList();
        return true;
    }
}
