using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class ScheduledTaskRunExpansionStateTests
{
    [Fact]
    public void Reset_默认展开最近一条运行()
    {
        var state = new ScheduledTaskRunExpansionState();

        state.Reset(["newest", "older", "oldest"]);

        Assert.Equal("newest", state.ExpandedRunId);
    }

    [Fact]
    public void SetExpanded_展开另一条时只保留新记录()
    {
        var state = new ScheduledTaskRunExpansionState();
        state.Reset(["newest", "older"]);

        state.SetExpanded("older", expanded: true);

        Assert.Equal("older", state.ExpandedRunId);
    }

    [Fact]
    public void SetExpanded_收起当前记录后没有展开项()
    {
        var state = new ScheduledTaskRunExpansionState();
        state.Reset(["newest"]);

        state.SetExpanded("newest", expanded: false);

        Assert.Null(state.ExpandedRunId);
    }

    [Fact]
    public void SetExpanded_忽略未知记录()
    {
        var state = new ScheduledTaskRunExpansionState();
        state.Reset(["newest"]);

        state.SetExpanded("missing", expanded: true);

        Assert.Equal("newest", state.ExpandedRunId);
    }
}
