using System.Collections.Generic;
using System.Linq;

namespace WithWindows.Core.Scheduling;

/// <summary>保证运行日志同时只展开一条记录。</summary>
public sealed class ScheduledTaskRunExpansionState
{
    private readonly HashSet<string> _runIds = [];

    public string? ExpandedRunId { get; private set; }

    public void Reset(IReadOnlyList<string> runIds)
    {
        _runIds.Clear();
        _runIds.UnionWith(runIds);
        ExpandedRunId = runIds.FirstOrDefault();
    }

    public void SetExpanded(string runId, bool expanded)
    {
        if (!_runIds.Contains(runId))
            return;

        if (expanded)
        {
            ExpandedRunId = runId;
        }
        else if (ExpandedRunId == runId)
        {
            ExpandedRunId = null;
        }
    }
}
