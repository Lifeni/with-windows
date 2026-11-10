using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WithWindows.Core.Interop;
using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

/// <summary>
/// 应用内定时调度宿主。只在应用运行期间工作，按最近截止时间布置单次计时器。
/// </summary>
public sealed class ScheduledTaskHost : IDisposable
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromDays(1);
    private static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(10);

    private readonly AppHost _host;
    private readonly ScheduledTaskCoordinator _coordinator;
    private readonly SchedulerLifecycleState _lifecycle = new();
    private readonly object _gate = new();
    private readonly HashSet<Task> _activeRuns = [];
    private readonly CancellationTokenSource _shutdown = new();
    private SystemTimeChangeMonitor? _timeChangeMonitor;
    private Timer? _timer;
    private int _generation;
    private DateTimeOffset? _claimNotBefore;
    private bool _processingDue;
    private bool _subscribed;
    private DateTimeOffset? _nextWakeAt;

    public ScheduledTaskHost(
        AppHost host,
        ScheduledTaskCoordinator coordinator)
    {
        _host = host;
        _coordinator = coordinator;
    }

    public DateTimeOffset? NextWakeAt
    {
        get
        {
            lock (_gate)
                return _nextWakeAt;
        }
    }

    public event EventHandler? EnvironmentChanged;

    public void Start()
    {
        if (!_lifecycle.TryStart())
            return;

        _coordinator.Tasks.Changed += OnTasksChanged;
        _timeChangeMonitor = new SystemTimeChangeMonitor(OnSystemTimeChanged);
        _subscribed = true;
        _claimNotBefore = DateTimeOffset.UtcNow;
        lock (_gate)
            ArmNextLocked(_claimNotBefore.Value);
        _host.Log.Info("Lua 脚本：调度宿主已启动");
    }

    public void RefreshSchedule()
    {
        lock (_gate)
        {
            if (!_lifecycle.TryBeginWork()
                || _processingDue)
            {
                return;
            }

            ProcessDueAndArmLocked(DateTimeOffset.UtcNow);
        }
    }

    public DateTimeOffset? GetNextRun(ScheduledTask task)
        => ScheduleCalculator.NextAfter(
            task.Schedule,
            DateTimeOffset.UtcNow,
            TimeZoneInfo.Local);

    private void OnTasksChanged(object? sender, EventArgs args)
        => RefreshSchedule();

    private void OnSystemTimeChanged()
    {
        TimeZoneInfo.ClearCachedData();
        _host.Log.Info("Lua 脚本：检测到系统时间或时区变化，重新布防");
        RefreshSchedule();
        EnvironmentChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTimerTick(object? state)
    {
        if (!_lifecycle.TryBeginWork())
            return;

        lock (_gate)
        {
            if (!_lifecycle.TryBeginWork()
                || _processingDue)
                return;

            _processingDue = true;
            bool armed = false;
            try
            {
                ProcessDueAndArmLocked(DateTimeOffset.UtcNow);
                armed = true;
            }
            catch (Exception ex)
            {
                _host.Log.Error($"Lua 脚本处理到期计划失败：{ex}");
            }
            finally
            {
                _processingDue = false;
                if (!armed)
                    ArmNextLocked(DateTimeOffset.UtcNow);
            }
        }
    }

    private void ProcessDueAndArmLocked(DateTimeOffset now)
    {
        if (!_lifecycle.TryBeginWork())
            return;

        IReadOnlyList<ScheduledTaskOccurrence> claims =
            ScheduledTaskClaimPolicy.GetDueClaims(
                _coordinator.Tasks.GetAll(),
                now,
                TimeZoneInfo.Local,
                _claimNotBefore);
        foreach (ScheduledTaskOccurrence occurrence in claims)
        {
            if (!_lifecycle.TryBeginWork())
                return;

            ScheduledTask? task = _coordinator.Tasks.Get(occurrence.TaskId);
            if (task is null)
                continue;

            double latenessMilliseconds =
                (now - occurrence.ScheduledFor).TotalMilliseconds;
            ScheduledTaskRunTrigger trigger = latenessMilliseconds > 1000
                ? ScheduledTaskRunTrigger.Late
                : ScheduledTaskRunTrigger.Scheduled;
            Task run = _coordinator.RunScheduledAsync(
                occurrence,
                trigger,
                _shutdown.Token);
            Track(run);
        }

        ArmNextLocked(now);
    }

    private void ArmNextLocked(DateTimeOffset now)
    {
        if (!_lifecycle.TryBeginWork())
        {
            _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _nextWakeAt = null;
            return;
        }

        int generation = ++_generation;
        _timer ??= new Timer(
            _ => OnTimerTick(null),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        DateTimeOffset[] candidates = _coordinator.Tasks.GetAll()
            .Select(task => ScheduleCalculator.NextAfter(
                task.Schedule,
                now,
                TimeZoneInfo.Local))
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToArray();

        if (candidates.Length == 0)
        {
            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _nextWakeAt = null;
            return;
        }

        DateTimeOffset next = candidates.Min();
        TimeSpan delay = next - now;
        if (delay < TimeSpan.FromMilliseconds(100))
            delay = TimeSpan.FromMilliseconds(100);
        else if (delay > MaximumDelay)
            delay = MaximumDelay;

        _nextWakeAt = next;
        _timer.Change(delay, Timeout.InfiniteTimeSpan);
        _host.Log.Info(
            $"Lua 脚本：下一次唤醒 {next.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
    }

    private void Track(Task task)
    {
        lock (_gate)
            _activeRuns.Add(task);

        _ = task.ContinueWith(
            completed =>
            {
                lock (_gate)
                    _activeRuns.Remove(completed);
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public async Task ShutdownAsync()
    {
        lock (_gate)
        {
            if (!_lifecycle.BeginStop())
                return;
            _coordinator.BeginShutdown();
            _shutdown.Cancel();
            _timer?.Dispose();
            _timer = null;
            _nextWakeAt = null;
            _generation++;
        }

        if (_subscribed)
        {
            _coordinator.Tasks.Changed -= OnTasksChanged;
            _timeChangeMonitor?.Dispose();
            _timeChangeMonitor = null;
            _subscribed = false;
        }

        await _coordinator.StopAllAsync();
        Task[] runs;
        lock (_gate)
            runs = _activeRuns.ToArray();

        try
        {
            await Task.WhenAll(runs).WaitAsync(ShutdownTimeout);
        }
        catch (TimeoutException ex)
        {
            _host.Log.Error($"等待 Lua 脚本停止超时：{ex.Message}");
        }
        catch (Exception ex)
        {
            _host.Log.Error($"等待 Lua 脚本停止失败：{ex.Message}");
        }

        _lifecycle.CompleteStop();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _lifecycle.Dispose();
            _shutdown.Cancel();
            _timer?.Dispose();
            _timer = null;
        }

        if (_subscribed)
        {
            _coordinator.Tasks.Changed -= OnTasksChanged;
            _timeChangeMonitor?.Dispose();
            _timeChangeMonitor = null;
            _subscribed = false;
        }

        _shutdown.Dispose();
    }
}
