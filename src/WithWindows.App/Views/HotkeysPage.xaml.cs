using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Controls;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Services;
using WithWindows.Tools;

namespace WithWindows.Views;

/// <summary>
/// 快捷键页：每个可绑热键的工具一行（工具名 + 录制框 + 启用开关 + 立即执行）。
/// 行由 <see cref="ToolCatalog"/> 生成；保存即热重载，注册失败在该行就地提示。
/// </summary>
public sealed partial class HotkeysPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "hotkeys";

    private readonly Dictionary<string, InfoBar> _infos = new(StringComparer.Ordinal);

    /// <summary>窄宽度阈值（DIP）：低于它时行内控件改为两行排列，避免「立即执行」被挤出可视区。</summary>
    private const double NarrowThreshold = 760;

    private readonly List<RowLayout> _rows = new();
    private bool _narrow;

    public HotkeysPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        SizeChanged += OnSizeChanged;
    }

    private static AppHost? Host => App.Host;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (RowsHost.Children.Count > 0 || Host is null)
            return;

        foreach (ToolEntry tool in ToolCatalog.Tools.Where(t => t.HasHotkey))
            RowsHost.Children.Add(BuildRow(tool));

        ApplyRowLayouts(force: true);
        ReportFailures(Host.ReloadBindings());
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e) => ApplyRowLayouts(force: false);

    /// <summary>按当前宽度决定每行是单行还是两行排列。</summary>
    private void ApplyRowLayouts(bool force)
    {
        bool narrow = ActualWidth < NarrowThreshold;
        if (!force && narrow == _narrow)
            return;

        _narrow = narrow;
        foreach (RowLayout row in _rows)
            row.Apply(narrow);
    }

    /// <summary>一行工具的可重排元素。</summary>
    private sealed record RowLayout(
        Grid Grid,
        FrameworkElement Title,
        FrameworkElement Input,
        FrameworkElement Enabled,
        FrameworkElement RunNow)
    {
        public void Apply(bool narrow)
        {
            Grid.ColumnDefinitions.Clear();
            Grid.RowDefinitions.Clear();
            Grid.Children.Clear();

            if (narrow)
            {
                // 窄：第一行「标题 + 启用」，第二行「录制框（拉伸）+ 清除 + 立即执行」
                Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                Grid.RowSpacing = 8;

                Place(Title, 0, 0);
                Place(Enabled, 0, 1);
                Place(Input, 1, 0);
                Place(RunNow, 1, 1);
                return;
            }

            // 宽：标题 | 录制框 | 启用开关 | 立即执行
            Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            Place(Title, 0, 0);
            Place(Input, 0, 1);
            Place(Enabled, 0, 2);
            Place(RunNow, 0, 3);

            void Place(FrameworkElement element, int row, int column)
            {
                Grid.SetRow(element, row);
                Grid.SetColumn(element, column);
                Grid.Children.Add(element);
            }
        }
    }

    private FrameworkElement BuildRow(ToolEntry tool)
    {
        AppConfig config = Host!.Config;
        ToolBinding? binding = config.Tools.FirstOrDefault(t => t.Id == tool.Id);

        var title = new TextBlock
        {
            Text = tool.Title,
            VerticalAlignment = VerticalAlignment.Center,
        };

        var input = new HotkeyInputBox
        {
            HotkeyText = binding?.Hotkey,
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(input, $"HotkeyBox_{tool.Id}");

        var enabled = new ToggleSwitch
        {
            IsOn = binding?.Enabled ?? true,
            OnContent = "启用",
            OffContent = "停用",
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(enabled, $"HotkeyEnabled_{tool.Id}");

        var runNow = new Button
        {
            Content = "立即执行",
            VerticalAlignment = VerticalAlignment.Center,
        };
        AutomationProperties.SetAutomationId(runNow, $"HotkeyRunNow_{tool.Id}");

        var info = new InfoBar
        {
            IsOpen = false,
            IsClosable = true,
            Severity = InfoBarSeverity.Warning,
        };
        AutomationProperties.SetAutomationId(info, $"HotkeyInfo_{tool.Id}");
        _infos[tool.Id] = info;

        input.HotkeyChanged += (_, value) =>
        {
            if (binding is null)
                return;

            binding.Hotkey = value;
            Host.SaveConfig();
            ReportFailures(Host.ReloadBindings());
        };

        enabled.Toggled += (_, _) =>
        {
            if (binding is null)
                return;

            binding.Enabled = enabled.IsOn;
            Host.SaveConfig();
            ReportFailures(Host.ReloadBindings());
        };

        runNow.Click += (_, _) => RunNow(tool, info);

        // 列定义与位置交给 RowLayout.Apply：宽一行、窄两行
        var grid = new Grid { ColumnSpacing = 12 };
        _rows.Add(new RowLayout(grid, title, input, enabled, runNow));

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(grid);
        content.Children.Add(info);

        return new Border
        {
            Padding = new Thickness(16),
            CornerRadius = new CornerRadius(6),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            Child = content,
        };
    }

    private void RunNow(ToolEntry tool, InfoBar info)
    {
        try
        {
            var router = new HotkeyRouter((MainWindow)App.MainWindow!, Host!.Config);
            ActionResult? result = router.Invoke(tool.Id);

            info.Severity = InfoBarSeverity.Success;
            info.Title = result?.Message ?? $"已打开{tool.Title}";
            info.IsOpen = true;
        }
        catch (Exception ex)
        {
            info.Severity = InfoBarSeverity.Error;
            info.Title = $"执行失败：{ex.Message}";
            info.IsOpen = true;
        }
    }

    /// <summary>把热重载失败原因显示到对应行；没有失败的行自动收起提示。</summary>
    private void ReportFailures(IReadOnlyList<string> failures)
    {
        foreach ((string toolId, InfoBar info) in _infos)
        {
            string? failure = failures.FirstOrDefault(f => f.StartsWith(toolId + "：", StringComparison.Ordinal));
            if (failure is null)
            {
                if (info.Severity != InfoBarSeverity.Success)
                    info.IsOpen = false;
                continue;
            }

            info.Severity = InfoBarSeverity.Warning;
            info.Title = failure;
            info.IsOpen = true;
        }
    }
}
