using System;
using System.Threading.Tasks;
using Windows.Devices.Geolocation;
using Windows.Foundation;

namespace WithWindows.Services;

/// <summary>主题自动切换定位结果；失败时保留可显示的中文原因。</summary>
public readonly record struct ThemeLocationResult(
    bool Success,
    double Latitude,
    double Longitude,
    string? Error);

/// <summary>
/// 通过 Windows 位置服务获取当前坐标。日出日落只需要城市级精度，因此优先使用
/// 无需精确位置授权的粗定位；所有 WinRT 调用都有硬超时，避免未打包应用卡在权限请求上。
/// 这里不尝试修改系统设置，也不回退到固定城市坐标。
/// </summary>
public static class ThemeLocationProvider
{
    private static readonly TimeSpan MaximumLocationAge = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan AccessRequestTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan PositionRequestTimeout = TimeSpan.FromSeconds(10);
    private const string LocationUnavailableMessage =
        "无法获取当前位置。请在 Windows 设置 > 隐私和安全性 > 位置 中打开位置服务，"
        + "并允许桌面应用访问位置后重试。";

    /// <summary>在 UI 线程请求当前位置；调用方负责在页面异步返回后检查页面是否仍挂载。</summary>
    public static async Task<ThemeLocationResult> GetCurrentAsync()
    {
        try
        {
            // 未打包桌面应用调用 RequestAccessAsync 可能不返回，而且日出日落不需要
            // 精确位置。先超时询问一次，再无条件尝试系统允许的粗定位回退。
            GeolocationAccessStatus access = await AwaitWinRtAsync(
                Geolocator.RequestAccessAsync(),
                AccessRequestTimeout);
            if (access == GeolocationAccessStatus.Denied)
                return await TryGetCoarsePositionAsync();

            var locator = new Geolocator
            {
                DesiredAccuracyInMeters = 10_000,
                ReportInterval = 0,
            };
            locator.AllowFallbackToConsentlessPositions();
            Geoposition? position = await AwaitWinRtAsync(
                locator.GetGeopositionAsync(
                    MaximumLocationAge,
                    PositionRequestTimeout),
                PositionRequestTimeout + TimeSpan.FromSeconds(2));
            if (position is null)
            {
                ThemeLocationResult coarse = await TryGetCoarsePositionAsync();
                if (coarse.Success)
                    return coarse;

                return new ThemeLocationResult(
                    false,
                    0,
                    0,
                    "获取当前位置超时。请检查 Windows 位置服务和网络连接后重试。");
            }

            double latitude = position.Coordinate.Point.Position.Latitude;
            double longitude = position.Coordinate.Point.Position.Longitude;
            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
                return new ThemeLocationResult(false, 0, 0, "系统返回的位置坐标无效。");

            return new ThemeLocationResult(true, latitude, longitude, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ThemeLocationResult(
                false,
                0,
                0,
                LocationUnavailableMessage);
        }
        catch (Exception ex)
        {
            return new ThemeLocationResult(
                false,
                0,
                0,
                $"无法读取当前位置：{ex.Message}");
        }
    }

    private static async Task<ThemeLocationResult> TryGetCoarsePositionAsync()
    {
        try
        {
            var locator = new Geolocator
            {
                DesiredAccuracyInMeters = 25_000,
                ReportInterval = 0,
            };
            locator.AllowFallbackToConsentlessPositions();
            Geoposition? position = await AwaitWinRtAsync(
                locator.GetGeopositionAsync(
                    MaximumLocationAge,
                    PositionRequestTimeout),
                PositionRequestTimeout + TimeSpan.FromSeconds(2));
            if (position is null)
            {
                return new ThemeLocationResult(
                    false,
                    0,
                    0,
                    "获取当前位置超时。请检查 Windows 位置服务和网络连接后重试。");
            }

            double latitude = position.Coordinate.Point.Position.Latitude;
            double longitude = position.Coordinate.Point.Position.Longitude;
            if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
                return new ThemeLocationResult(false, 0, 0, "系统返回的位置坐标无效。");

            return new ThemeLocationResult(true, latitude, longitude, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new ThemeLocationResult(false, 0, 0, LocationUnavailableMessage);
        }
        catch (Exception ex)
        {
            return new ThemeLocationResult(
                false,
                0,
                0,
                $"无法读取当前位置：{ex.Message}");
        }
    }

    /// <summary>
    /// 等待 WinRT 异步操作，超时后取消并观察未完成任务的异常。
    /// 未打包 WinUI 的位置调用会偶发永不完成，不能直接 await。
    /// </summary>
    private static async Task<T> AwaitWinRtAsync<T>(
        IAsyncOperation<T> operation,
        TimeSpan timeout)
    {
        Task<T> task = operation.AsTask();
        Task completed = await Task.WhenAny(task, Task.Delay(timeout));
        if (!ReferenceEquals(completed, task))
        {
            try
            {
                operation.Cancel();
            }
            catch
            {
                // 操作可能已经完成或无法取消；超时结果已经确定。
            }

            _ = task.ContinueWith(
                static pending => { _ = pending.Exception; },
                TaskScheduler.Default);
            return default!;
        }

        return await task;
    }
}
