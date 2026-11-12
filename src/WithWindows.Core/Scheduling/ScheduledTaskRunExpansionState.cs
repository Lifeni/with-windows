using System.Collections.Generic;
using System.Linq;

namespace WithWindows.Core.Scheduling;

/// <summary>记录运行日志中可同时展开的多条记录。</summary>
public sealed class ScheduledTaskRunExpansionState
{
    private readonly List<string> _runIds = [];
    private readonly HashSet<string> _expandedRunIds = [];

    /// <summary>最近一次展开的记录，用于弹窗打开后的默认焦点。</summary>
    public string? ExpandedRunId { get; private set; }

    public void Reset(IReadOnlyList<string> runIds)
    {
        _runIds.Clear();
        _runIds.AddRange(runIds);
        _expandedRunIds.Clear();
        ExpandedRunId = _runIds.FirstOrDefault();
        if (ExpandedRunId is { } firstRunId)
            _expandedRunIds.Add(firstRunId);
    }

    public bool IsExpanded(string runId)
        => _expandedRunIds.Contains(runId);

    public void SetExpanded(string runId, bool expanded)
    {
        if (!_runIds.Contains(runId))
            return;

        if (expanded)
        {
            _expandedRunIds.Add(runId);
            ExpandedRunId = runId;
        }
        else
        {
            _expandedRunIds.Remove(runId);
            if (ExpandedRunId == runId)
            {
                ExpandedRunId = _runIds.FirstOrDefault(
                    _expandedRunIds.Contains);
            }
        }
    }
}
