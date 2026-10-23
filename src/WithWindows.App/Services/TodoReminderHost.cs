using System;
using System.Linq;
using System.Threading;
using WithWindows.Core.Todos;

namespace WithWindows.Services;

/// <summary>
/// 待办提醒调度。只在最近提醒的截止时间启动一次后台计时器；
/// 没有待提醒项时不保持唤醒。
/// </summary>
public sealed class TodoReminderHost : IDisposable
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromDays(1);

    private readonly AppHost _host;
    private readonly TodoStore _store;
    private Timer? _timer;
    private readonly Action<IReadOnlyList<string>> _showReminder;
    private readonly object _scheduleGate = new();

    private bool _started;
    private bool _processingDue;
    private bool _disposed;

    public TodoReminderHost(
        AppHost host,
        Action<IReadOnlyList<string>> showReminder)
    {
        _host = host;
        _store = host.Todos;
        _showReminder = showReminder;
    }

    public void Start()
    {
        if (_started || _disposed)
            return;

        _started = true;
        _store.Changed += OnTodosChanged;
        RefreshSchedule();
    }

    /// <summary>重新按最早的提醒时间设置单次计时器；由待办变更或提醒处理后调用。</summary>
    public void RefreshSchedule()
    {
        if (_disposed)
            return;

        lock (_scheduleGate)
        {
            try
            {
                _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                DateTimeOffset now = DateTimeOffset.UtcNow;
                DateTimeOffset? next = TodoReminderPolicy.NextWakeAt(
                    _store.GetAll(),
                    now);
                if (next is null)
                {
                    _timer?.Dispose();
                    _timer = null;
                    _host.Log.Info("待办提醒：没有待处理提醒，计时器已停止");
                    return;
                }

                TimeSpan delay = next.Value - now;
                if (delay < TimeSpan.FromSeconds(1))
                    delay = TimeSpan.FromSeconds(1);
                else if (delay > MaximumDelay)
                    delay = MaximumDelay;

                _timer ??= new Timer(
                    state => OnTimerTick((TodoReminderHost)state!),
                    this,
                    Timeout.InfiniteTimeSpan,
                    Timeout.InfiniteTimeSpan);
                _timer.Change(delay, Timeout.InfiniteTimeSpan);
                _host.Log.Info(
                    $"待办提醒：下一次唤醒 {next.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private void OnTodosChanged(object? sender, EventArgs args)
    {
        if (!_processingDue)
            RefreshSchedule();
    }

    private void OnTimerTick(TodoReminderHost host)
    {
        if (_disposed || _processingDue)
            return;

        _processingDue = true;
        try
        {
            IReadOnlyList<TodoItem> due = TodoReminderPolicy.GetDue(
                _store.GetAll(),
                DateTimeOffset.UtcNow);
            if (due.Count == 0)
                return;

            _showReminder(
                due.Select(item => item.Title)
                    .Where(title => !string.IsNullOrWhiteSpace(title))
                    .ToArray());

            foreach (TodoItem item in due)
            {
                try
                {
                    _store.MarkReminderDelivered(item.Id, DateTimeOffset.UtcNow);
                }
                catch (Exception ex)
                {
                    _host.Log.Error(
                        $"待办“{item.Title}”提醒状态保存失败：{ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _host.Log.Error($"待办提醒处理失败：{ex}");
        }
        finally
        {
            _processingDue = false;
            RefreshSchedule();
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer?.Dispose();
        _timer = null;
        _store.Changed -= OnTodosChanged;
    }
}
