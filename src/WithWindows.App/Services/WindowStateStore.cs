using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;
using WithWindows.Core.Config;
using WithWindows.Core.Interop;

namespace WithWindows.Services;

/// <summary>
/// 主窗口尺寸与位置记忆。位置为 null 表示尚未保存（屏幕左上角 (0, 0) 是合法坐标，不能用 0 当哨兵）；
/// 恢复前校验目标位置仍在可见显示器内，超出时移回主屏居中。
/// </summary>
public sealed class WindowStateStore : IDisposable
{
    private const int MinWidth = 640;
    private const int MinHeight = 480;
    private static readonly TimeSpan PersistDebounce = TimeSpan.FromMilliseconds(500);

    private readonly Window _window;
    private readonly AppConfig _config;
    private readonly Action _save;
    private readonly DispatcherQueueTimer _debounce;
    private bool _restoring;

    public WindowStateStore(Window window, AppConfig config, Action save)
    {
        _window = window;
        _config = config;
        _save = save;

        _debounce = window.DispatcherQueue.CreateTimer();
        _debounce.Interval = PersistDebounce;
        _debounce.IsRepeating = false;
        _debounce.Tick += (_, _) => Persist();
    }

    /// <summary>当前 DPI 缩放（DIP → 物理像素）。</summary>
    private double Scale => DisplayScale.ForWindow(Microsoft.UI.Win32Interop.GetWindowFromWindowId(_window.AppWindow.Id));

    /// <summary>设置最小尺寸（必须用 OverlappedPresenter 的 PreferredMinimum*，AppWindow 上没有这两个属性）。</summary>
    public void ApplyMinimumSize()
    {
        if (_window.AppWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinWidth;
            presenter.PreferredMinimumHeight = MinHeight;
        }
    }

    /// <summary>
    /// 按配置恢复尺寸与位置。配置里的宽高是 **DIP**（与 WinUI 一致），应用前按当前缩放换算成物理像素——
    /// 否则同一份配置在不同缩放率的屏幕上视觉大小不同（150% 下 1100 物理像素只有约 733 DIP，
    /// 会掉进 NavigationView 的紧凑区间，左栏只剩图标）。
    /// </summary>
    public void Restore()
    {
        WindowStateConfig state = _config.WindowState;
        _restoring = true;
        try
        {
            double scale = Scale;
            int width = (int)Math.Round(Math.Max(state.Width, MinWidth) * scale);
            int height = (int)Math.Round(Math.Max(state.Height, MinHeight) * scale);

            if (state.X is double x && state.Y is double y)
            {
                // 位置本身按物理像素保存（屏幕坐标），这里不需要换算
                var point = new PointInt32((int)x, (int)y);
                if (IsVisible(point))
                {
                    _window.AppWindow.MoveAndResize(new RectInt32(point.X, point.Y, width, height));
                    return;
                }

                // 位置已不在任何显示器内：回到主屏居中
                RectInt32 work = DisplayArea.GetFromWindowId(_window.AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
                _window.AppWindow.MoveAndResize(new RectInt32(
                    work.X + Math.Max(0, (work.Width - width) / 2),
                    work.Y + Math.Max(0, (work.Height - height) / 2),
                    width,
                    height));
                return;
            }

            _window.AppWindow.Resize(new SizeInt32(width, height));
        }
        finally
        {
            _restoring = false;
        }
    }

    /// <summary>窗口尺寸/位置变化后去抖写回配置。</summary>
    public void OnWindowChanged()
    {
        if (_restoring)
            return;

        _debounce.Stop();
        _debounce.Start();
    }

    /// <summary>立即写回当前尺寸与位置。</summary>
    public void Persist()
    {
        _debounce.Stop();

        try
        {
            AppWindow appWindow = _window.AppWindow;
            SizeInt32 size = appWindow.Size;
            PointInt32 position = appWindow.Position;

            double scale = Scale;
            _config.WindowState.Width = Math.Round(size.Width / scale);
            _config.WindowState.Height = Math.Round(size.Height / scale);
            _config.WindowState.X = position.X;
            _config.WindowState.Y = position.Y;
            _save();
        }
        catch (Exception)
        {
            // 取窗口状态失败不影响退出流程
        }
    }

    public void Dispose() => Persist();

    /// <summary>判断给定屏幕坐标是否落在任一显示器的工作区内。</summary>
    private static bool IsVisible(PointInt32 point)
    {
        try
        {
            foreach (DisplayArea area in DisplayArea.FindAll())
            {
                RectInt32 work = area.WorkArea;
                if (point.X >= work.X && point.X < work.X + work.Width
                    && point.Y >= work.Y && point.Y < work.Y + work.Height)
                    return true;
            }

            return false;
        }
        catch (Exception)
        {
            // DisplayArea.FindAll 在窗口打开时可能抛 InvalidCastException：失败时按可见处理，不阻塞窗口打开
            return true;
        }
    }
}
