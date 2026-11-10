using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class SchedulerLifecycleStateTests
{
    [Fact]
    public void BeginStop_拒绝新的工作()
    {
        var state = new SchedulerLifecycleState();
        Assert.True(state.TryStart());
        Assert.True(state.TryBeginWork());

        state.BeginStop();

        Assert.False(state.TryBeginWork());
        Assert.Equal(SchedulerLifecycle.Stopping, state.Current);
    }

    [Fact]
    public void CompleteStop_再次开始时重置状态()
    {
        var state = new SchedulerLifecycleState();
        state.TryStart();
        state.BeginStop();
        state.CompleteStop();

        Assert.Equal(SchedulerLifecycle.Stopped, state.Current);
        Assert.True(state.TryStart());
    }
}
