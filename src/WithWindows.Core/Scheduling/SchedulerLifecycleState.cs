namespace WithWindows.Core.Scheduling;

public enum SchedulerLifecycle
{
    Stopped,
    Running,
    Stopping,
    Disposed,
}

public sealed class SchedulerLifecycleState
{
    private readonly object _gate = new();
    private SchedulerLifecycle _current = SchedulerLifecycle.Stopped;

    public SchedulerLifecycle Current
    {
        get
        {
            lock (_gate)
                return _current;
        }
    }

    public bool TryStart()
    {
        lock (_gate)
        {
            if (_current != SchedulerLifecycle.Stopped)
                return false;
            _current = SchedulerLifecycle.Running;
            return true;
        }
    }

    public bool TryBeginWork()
    {
        lock (_gate)
            return _current == SchedulerLifecycle.Running;
    }

    public bool BeginStop()
    {
        lock (_gate)
        {
            if (_current is SchedulerLifecycle.Stopping or SchedulerLifecycle.Disposed)
                return false;
            _current = SchedulerLifecycle.Stopping;
            return true;
        }
    }

    public void CompleteStop()
    {
        lock (_gate)
        {
            if (_current == SchedulerLifecycle.Stopping)
                _current = SchedulerLifecycle.Stopped;
        }
    }

    public void Dispose()
    {
        lock (_gate)
            _current = SchedulerLifecycle.Disposed;
    }
}
