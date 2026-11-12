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
    public void SetExpanded_可以同时展开多条记录()
    {
        var state = new ScheduledTaskRunExpansionState();
        state.Reset(["newest", "older"]);

        state.SetExpanded("older", expanded: true);

        Assert.True(state.IsExpanded("newest"));
        Assert.True(state.IsExpanded("older"));
        Assert.Equal("older", state.ExpandedRunId);
    }

    [Fact]
    public void SetExpanded_收起一条后其他记录仍保持展开()
    {
        var state = new ScheduledTaskRunExpansionState();
        state.Reset(["newest", "older"]);
        state.SetExpanded("older", expanded: true);

        state.SetExpanded("newest", expanded: false);

        Assert.False(state.IsExpanded("newest"));
        Assert.True(state.IsExpanded("older"));
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
