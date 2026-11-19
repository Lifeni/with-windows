using System;
using System.Threading;
using System.Threading.Tasks;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Core.Theme;

namespace WithWindows.Services;

/// <summary>
/// 应用内亮暗主题自动切换宿主。按保存的当前位置计算日出日落，只在进程存活期间工作：
/// 关闭主窗口驻留托盘时继续，完全退出后停止；计时器迟到时按当前时刻重新对账。
/// </summary>
public sealed class ThemeSwitchHost : IDisposable
{
    private static readonly TimeSpan MaximumDelay = TimeSpan.FromDays(1);
    private static readonly TimeSpan MinimumDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan PolarReviewDelay = TimeSpan.FromHours(6);

    private readonly AppHost _host;
    private readonly object _gate = new();
    private Timer? _timer;
    private DateTimeOffset? _nextDueAt;
    private bool _started;
    private bool _disposed;

    public ThemeSwitchHost(AppHost host) => _host = host;

    public void Start()
    {
        lock (_gate)
        {
            if (_started || _disposed)
                return;
            _started = true;
        }

        QueueEvaluation(applyCurrent: true);
    }

    /// <summary>配置变化后重新布防；页面开启自动切换或关闭时调用。</summary>
    public void RefreshSchedule()
    {
        lock (_gate)
        {
            if (!_started || _disposed)
                return;
        }

        // 开关自动切换只重新布防；只有当前主题与计划目标确实不一致时才应用，
        // 避免每次拨动开关都重新设置主题和壁纸，冲掉用户手动选择的壁纸与契合度。
        QueueEvaluation(applyCurrent: false);
    }

    /// <summary>
    /// 启动与拨动开关都发生在 UI 线程。主题写入和壁纸替换可能等待 Shell，
    /// 不能在调用方线程同步执行，否则主窗口会短暂无响应。
    /// </summary>
    private void QueueEvaluation(bool applyCurrent)
    {
        _ = Task.Run(() =>
        {
            lock (_gate)
            {
                if (_disposed)
                    return;

                try
                {
                    RefreshScheduleLocked(DateTimeOffset.UtcNow, applyCurrent);
                }
                catch (Exception ex)
                {
                    DisarmLocked();
                    _host.Log.Error($"自动主题刷新失败：{ex.Message}");
                }
            }
        });
    }

    private void OnTimerTick(object? state)
    {
        lock (_gate)
        {
            if (_disposed)
                return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (_nextDueAt is { } due && now < due)
            {
                ArmLocked(due, now);
                return;
            }

            RefreshScheduleLocked(now, applyCurrent: false);
        }
    }

    private void RefreshScheduleLocked(DateTimeOffset now, bool applyCurrent)
    {
        if (_disposed)
            return;

        ThemeSwitchConfig config = _host.Config.ThemeSwitch;
        if (!config.ScheduleEnabled)
        {
            DisarmLocked();
            return;
        }

        bool hasCoordinates = config.TryGetCoordinates(
            out double latitude,
            out double longitude);
        ThemeScheduleOccurrence? next;
        try
        {
            ThemeMode? target = hasCoordinates
                ? ThemeScheduleCalculator.TargetAt(
                    now,
                    latitude,
                    longitude,
                    TimeZoneInfo.Local)
                : ThemeScheduleDefaults.TargetAt(now, TimeZoneInfo.Local);
            if (target is { } desired
                && (applyCurrent || ThemeToggleAction.CurrentModeOrDefault() != desired))
            {
                var action = new ThemeToggleAction(config);
                ActionResult result = action.Execute(
                    desired == ThemeMode.Light ? "light" : "dark");
                _host.Log.Info(
                    $"自动主题（{(hasCoordinates ? "日出日落" : "默认时间")}）：{result.Message}");
            }

            next = hasCoordinates
                ? ThemeScheduleCalculator.NextAfter(
                    now,
                    latitude,
                    longitude,
                    TimeZoneInfo.Local)
                : ThemeScheduleDefaults.NextAfter(now, TimeZoneInfo.Local);
        }
        catch (Exception ex)
        {
            DisarmLocked();
            _host.Log.Error($"自动主题计算失败：{ex.Message}");
            return;
        }

        _timer ??= new Timer(
            state => OnTimerTick(state),
            null,
            Timeout.InfiniteTimeSpan,
            Timeout.InfiniteTimeSpan);

        if (next is { } occurrence)
        {
            ArmLocked(occurrence.At, now);
            _host.Log.Info(
                hasCoordinates
                    ? $"自动主题：下一次唤醒 {occurrence.At.ToLocalTime():yyyy-MM-dd HH:mm}，"
                      + $"目标{(occurrence.ToLight ? "亮色" : "暗色")}"
                    : $"自动主题：定位不可用，按默认时间 {ThemeScheduleDefaults.DisplayRange}；"
                      + $"下一次唤醒 {occurrence.At.ToLocalTime():yyyy-MM-dd HH:mm}，"
                      + $"目标{(occurrence.ToLight ? "亮色" : "暗色")}");
            return;
        }

        // 极昼、极夜或极区短时无日出日落：定时复盘，避免一直睡到应用退出。
        DateTimeOffset reviewAt = now.Add(PolarReviewDelay);
        ArmLocked(reviewAt, now);
        _host.Log.Info(
            $"自动主题：当前无日出日落切换点，{reviewAt.ToLocalTime():yyyy-MM-dd HH:mm} 后复盘。");
    }

    private void ArmLocked(DateTimeOffset due, DateTimeOffset now)
    {
        _nextDueAt = due;
        TimeSpan delay = due - now;
        if (delay < MinimumDelay)
            delay = MinimumDelay;
        else if (delay > MaximumDelay)
            delay = MaximumDelay;
        _timer?.Change(delay, Timeout.InfiniteTimeSpan);
    }

    private void DisarmLocked()
    {
        _nextDueAt = null;
        _timer?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
            _nextDueAt = null;
        }
    }
}
