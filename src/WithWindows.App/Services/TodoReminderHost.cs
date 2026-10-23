using System;
using Microsoft.UI.Dispatching;
using Microsoft.Windows.AppLifecycle;
using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;
using WithWindows.Core.Todos;

namespace WithWindows.Services;

/// <summary>
/// 待办提醒宿主。只在最近提醒的截止时间启动一次计时器，没有待提醒项时不保持唤醒。
/// </summary>
public sealed class TodoReminderHost : IDisposable
{
    private const string OpenTodosAction = "open_todos";
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromDays(1);

    private readonly AppHost _host;
    private readonly TodoStore _store;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _timer;
    private readonly Action _openTodos;

    private bool _started;
    private bool _processingDue;
    private bool _subscribedToNotifications;
    private bool _disposed;

    public TodoReminderHost(
        AppHost host,
        DispatcherQueue dispatcher,
        Action openTodos)
    {
        _host = host;
        _store = host.Todos;
        _dispatcher = dispatcher;
        _openTodos = openTodos;
        _timer = dispatcher.CreateTimer();
        _timer.IsRepeating = false;
        _timer.Tick += OnTimerTick;
    }

    /// <summary>是否成功注册系统通知。</summary>
    public bool NotificationsAvailable { get; private set; }

    public void Start()
    {
        if (_started || _disposed)
            return;

        _started = true;
        _store.Changed += OnTodosChanged;

        try
        {
            if (!AppNotificationManager.IsSupported())
            {
                _host.Log.Error("待办提醒不可用：当前系统不支持 Windows 应用通知。");
            }
            else
            {
                AppNotificationManager notifications = AppNotificationManager.Default;
                notifications.NotificationInvoked += OnNotificationInvoked;
                _subscribedToNotifications = true;
                notifications.Register();
                NotificationsAvailable = true;
                _host.Log.Info("待办系统通知已注册");

                // Register 必须先于 GetActivatedEventArgs；应用由通知冷启动时据此打开待办页。
                AppActivationArguments activation =
                    AppInstance.GetCurrent().GetActivatedEventArgs();
                if (activation.Kind == ExtendedActivationKind.AppNotification)
                    _dispatcher.TryEnqueue(() => _openTodos());
            }
        }
        catch (Exception ex)
        {
            NotificationsAvailable = false;
            _host.Log.Error($"待办系统通知注册失败：{ex.Message}");
        }

        RefreshSchedule();
    }

    /// <summary>重新按最早的提醒时间设置单次计时器；由待办变更或提醒处理后调用。</summary>
    public void RefreshSchedule()
    {
        if (_disposed)
            return;

        if (!_dispatcher.HasThreadAccess)
        {
            _dispatcher.TryEnqueue(RefreshSchedule);
            return;
        }

        _timer.Stop();
        IReadOnlyList<TodoItem> items = _store.GetAll();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset? next = TodoReminderPolicy.NextWakeAt(items, now);
        if (next is null)
        {
            _host.Log.Info("待办提醒：没有待处理提醒，计时器已停止");
            return;
        }

        TimeSpan delay = next.Value - now;
        if (delay < TimeSpan.FromSeconds(1))
            delay = TimeSpan.FromSeconds(1);
        else if (delay > MaximumDelay)
            delay = MaximumDelay;

        _timer.Interval = delay;
        _timer.Start();
        _host.Log.Info($"待办提醒：下一次唤醒 {next.Value.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
    }

    private void OnTodosChanged(object? sender, EventArgs args)
    {
        if (!_processingDue)
            RefreshSchedule();
    }

    private void OnTimerTick(DispatcherQueueTimer sender, object args)
    {
        _timer.Stop();
        _processingDue = true;
        try
        {
            IReadOnlyList<TodoItem> due = TodoReminderPolicy.GetDue(
                _store.GetAll(),
                DateTimeOffset.UtcNow);
            foreach (TodoItem item in due)
            {
                try
                {
                    ShowReminder(item);
                }
                catch (Exception ex)
                {
                    _host.Log.Error($"待办“{item.Title}”提醒失败：{ex}");
                }

                // 无论系统通知是否可用都记为该提醒已处理，避免同一到期项形成紧凑重试循环。
                try
                {
                    _store.MarkReminderDelivered(item.Id, DateTimeOffset.UtcNow);
                }
                catch (Exception ex)
                {
                    _host.Log.Error($"待办“{item.Title}”提醒状态保存失败：{ex.Message}");
                }
            }
        }
        finally
        {
            _processingDue = false;
            RefreshSchedule();
        }
    }

    private void ShowReminder(TodoItem item)
    {
        if (!NotificationsAvailable)
        {
            _host.Log.Error($"待办“{item.Title}”到期，但系统通知不可用。");
            return;
        }

        DateTimeOffset reminderAt = item.ReminderAt ?? DateTimeOffset.Now;
        AppNotification notification = new AppNotificationBuilder()
            .AddArgument("action", OpenTodosAction)
            .AddText(item.Title)
            .AddText($"待办提醒：{reminderAt.ToLocalTime():yyyy-MM-dd HH:mm}")
            .SetTag(item.Id)
            .BuildNotification();
        AppNotificationManager.Default.Show(notification);
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        if (args.Arguments.TryGetValue("action", out string? action)
            && string.Equals(action, OpenTodosAction, StringComparison.Ordinal))
        {
            _dispatcher.TryEnqueue(() => _openTodos());
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTimerTick;
        _store.Changed -= OnTodosChanged;
        if (_subscribedToNotifications)
        {
            AppNotificationManager.Default.NotificationInvoked -= OnNotificationInvoked;
            _subscribedToNotifications = false;
        }
    }
}
