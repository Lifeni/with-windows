using System.Globalization;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using WithWindows.Config;
using WithWindows.Core;

namespace WithWindows.Actions;

/// <summary>
/// 自动亮暗切换的设置：默认内置坐标（未配置即用），需要覆盖时在 config.json 加
/// auto_theme 条目（声明式、无热键）。两种覆盖模式：latitude/longitude（日出日落按日期计算，
/// 时区固定北京时间 UTC+8，不随机器时区变化）或 sunrise/sunset（固定时间 "HH:mm"）。offset_minutes 可选，对切换点整体偏移（正=延后）。
/// </summary>
public sealed class AutoThemeSettings
{
    /// <summary>内置默认坐标（未配置时使用；时区固定北京时间，不随坐标/机器变化）。</summary>
    public const double DefaultLatitude = 36.6512;
    public const double DefaultLongitude = 117.1201;

    /// <summary>固定北京时间偏移（中国标准时间，无夏令时）。日出日落按北京时间判定，不随机器时区变化。</summary>
    public static readonly TimeSpan ChinaOffset = TimeSpan.FromHours(8);

    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public TimeSpan? FixedSunrise { get; private set; }
    public TimeSpan? FixedSunset { get; private set; }
    public TimeSpan Offset { get; private set; }

    public bool HasCoordinates => Latitude is not null && Longitude is not null;
    public bool HasFixedTimes => FixedSunrise is not null && FixedSunset is not null;

    public static AutoThemeSettings FromArgs(object? args)
    {
        var settings = new AutoThemeSettings();
        if (args is JsonObject obj)
        {
            if (TryParseDouble(obj, "latitude", out double lat)) settings.Latitude = lat;
            if (TryParseDouble(obj, "longitude", out double lon)) settings.Longitude = lon;
            if (TryParseInt(obj, "offset_minutes", out int offset)) settings.Offset = TimeSpan.FromMinutes(offset);
            if (TryParseTime(obj, "sunrise", out TimeSpan rise)) settings.FixedSunrise = rise;
            if (TryParseTime(obj, "sunset", out TimeSpan set)) settings.FixedSunset = set;
        }

        // 未配置坐标或固定时间：回退到内置默认坐标（北京），开箱即用
        if (!settings.HasCoordinates && !settings.HasFixedTimes)
        {
            settings.Latitude = DefaultLatitude;
            settings.Longitude = DefaultLongitude;
        }
        return settings;
    }

    /// <summary>校验配置；返回错误信息，合法返回 null（缺配置时已回退内置坐标，不再报"未配置"）。</summary>
    public string? Validate()
    {
        if (HasCoordinates)
        {
            if (Latitude!.Value < -90 || Latitude.Value > 90)
                return "latitude 超出范围 [-90, 90]";
            if (Longitude!.Value < -180 || Longitude.Value > 180)
                return "longitude 超出范围 [-180, 180]";
        }
        if (HasFixedTimes && FixedSunrise >= FixedSunset)
            return "sunrise 必须早于 sunset";
        return null;
    }

    /// <summary>给定本地时刻，返回该日的日出/日落（本地时间）与极昼/极夜全天目标。</summary>
    public (DateTime? Sunrise, DateTime? Sunset, string? AllDay) GetDayTimes(DateTime localNow)
    {
        if (FixedSunrise is not null && FixedSunset is not null)
            return (localNow.Date + FixedSunrise.Value + Offset, localNow.Date + FixedSunset.Value + Offset, null);

        if (Latitude is null || Longitude is null)
            return (null, null, null);

        var times = SunTimes.GetDayTimes(localNow.Date, Latitude.Value, Longitude.Value, ChinaOffset);
        return (times.Sunrise + Offset, times.Sunset + Offset, times.AllDay);
    }

    private static bool TryParseDouble(JsonObject obj, string key, out double value)
    {
        value = 0;
        return obj.TryGet(key, out var v) && v is JsonString s
            && double.TryParse(s.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseInt(JsonObject obj, string key, out int value)
    {
        value = 0;
        return obj.TryGet(key, out var v) && v is JsonString s
            && int.TryParse(s.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseTime(JsonObject obj, string key, out TimeSpan value)
    {
        value = default;
        return obj.TryGet(key, out var v) && v is JsonString s
            && TimeSpan.TryParseExact(s.Value, new[] { "h\\:mm", "hh\\:mm" }, CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>
/// 按日出日落自动切换亮/暗的后台调度器。启用状态持久化到 HKCU\Software\WithWindows\AutoTheme 的
/// Enabled 标志（托盘菜单勾选开关，重启后保持）。WinForms 定时器在 UI 线程上按"下一切换点"调度；
/// 错过切换点（睡眠唤醒等）时按当前时刻对账一次主题。极昼/极夜日无切换点，半天后重算。
/// </summary>
public sealed class AutoThemeScheduler : IDisposable
{
    private const string FlagKey = @"HKEY_CURRENT_USER\Software\WithWindows\AutoTheme";
    private const string EnabledValue = "Enabled";

    private static readonly TimeSpan MaxReschedule = TimeSpan.FromHours(24);
    private static readonly TimeSpan MinReschedule = TimeSpan.FromMinutes(1);

    private readonly IAction _themeAction;
    private readonly AutoThemeSettings _settings;
    private readonly Logger _log;
    private readonly DispatcherQueueTimer _timer;
    private bool _enabled;

    public bool Enabled => _enabled;

    public AutoThemeScheduler(IAction themeAction, AutoThemeSettings settings, Logger log, DispatcherQueue queue)
    {
        _themeAction = themeAction;
        _settings = settings;
        _log = log;
        _timer = queue.CreateTimer();
        _timer.Tick += (_, _) => OnTick();
    }

    /// <summary>启用：校验设置、持久化标志、立即对账一次当前主题，随后按日出日落调度。
    /// 返回错误信息，成功返回 null（不弹通知，由调用方决定提示方式）。</summary>
    public string? TryStart()
    {
        string? error = _settings.Validate();
        if (error is not null)
            return error;

        SetEnabledFlag(true);
        _enabled = true;
        _timer.Interval = TimeSpan.FromMinutes(1);
        _timer.Start();
        OnTick();
        _log.Info("自动亮暗切换已启用");
        return null;
    }

    public void Stop()
    {
        _timer.Stop();
        _enabled = false;
        SetEnabledFlag(false);
        _log.Info("自动亮暗切换已停用");
    }

    public static bool GetEnabledFlag()
        => Registry.GetValue(FlagKey, EnabledValue, 0) is int i && i != 0;

    public static void SetEnabledFlag(bool enabled)
        => Registry.SetValue(FlagKey, EnabledValue, enabled ? 1 : 0, RegistryValueKind.DWord);

    private void OnTick()
    {
        try
        {
            // 统一按北京时间判定：机器时区可能不是 UTC+8，用 UTC 换算避免偏差
            DateTime now = DateTime.UtcNow + AutoThemeSettings.ChinaOffset;
            var (sunrise, sunset, allDay) = _settings.GetDayTimes(now);
            string? target = TargetFor(now, sunrise, sunset, allDay);

            if (target is not null && ThemeAction.GetCurrentMode() != target)
            {
                var result = _themeAction.Execute(target);
                if (result.Changed)
                    _log.Info($"[auto_theme] {result.Message}");
            }

            Reschedule(now, sunrise, sunset, allDay);
        }
        catch (Exception ex)
        {
            // 调度异常不崩溃：记录后 1 小时重试
            _log.Error($"[auto_theme] 调度失败: {ex}");
            _timer.Interval = TimeSpan.FromHours(1);
        }
    }

    /// <summary>当前时刻应处的主题模式（"light"/"dark"）；无法判定返回 null。纯函数。</summary>
    internal static string? TargetFor(DateTime now, DateTime? sunrise, DateTime? sunset, string? allDay)
    {
        if (allDay is not null)
            return allDay;
        if (sunrise is null || sunset is null)
            return null;
        return now < sunrise ? "dark" : now < sunset ? "light" : "dark";
    }

    /// <summary>下一切换时刻：今日日出/日落中未来最近的一个；都过了则取明日日出。纯函数。</summary>
    internal static DateTime? NextSwitch(DateTime now, DateTime? sunrise, DateTime? sunset, DateTime? tomorrowSunrise)
    {
        if (sunrise is not null && now < sunrise) return sunrise;
        if (sunset is not null && now < sunset) return sunset;
        return tomorrowSunrise;
    }

    private void Reschedule(DateTime now, DateTime? sunrise, DateTime? sunset, string? allDay)
    {
        if (allDay is not null)
        {
            // 极昼/极夜：无切换点，半天后重算（次日可能恢复正常）
            _timer.Interval = TimeSpan.FromHours(12);
            return;
        }

        var tomorrow = _settings.GetDayTimes(now.AddDays(1));
        DateTime? next = NextSwitch(now, sunrise, sunset, tomorrow.Sunrise);
        if (next is null)
        {
            _timer.Interval = TimeSpan.FromHours(12);
            return;
        }

        TimeSpan wait = next.Value - now;
        if (wait < MinReschedule) wait = MinReschedule;
        if (wait > MaxReschedule) wait = MaxReschedule;
        _timer.Interval = wait;
    }

    public void Dispose() => _timer.Stop();
}
