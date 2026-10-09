using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using WithWindows.Core.Actions;
using WithWindows.Core.Config;
using WithWindows.Services;

namespace WithWindows.Controls;

/// <summary>
/// 一条可绑热键的工具行：标题 + 录制框 + 启用开关 + 立即执行，注册失败就地提示。
/// 由工具所在页面直接使用（投屏页的「投屏」、设置页的「呼出主窗口」）。
/// </summary>
public sealed class HotkeyRow : UserControl
{
    private readonly InfoBar _info = new()
    {
        IsOpen = false,
        IsClosable = true,
        Severity = InfoBarSeverity.Warning,
    };

    private readonly HotkeyInputBox _box = new()
    {
        MinWidth = 160,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly ToggleSwitch _enabled = new()
    {
        OnContent = "启用",
        OffContent = "停用",
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly Button _run = new()
    {
        Content = "立即执行",
        VerticalAlignment = VerticalAlignment.Center,
    };

    private readonly CardRow _card = new();
    private ToolBinding? _binding;
    private string _runButtonText = "立即执行";

    public HotkeyRow()
    {
        Build();
        Loaded += OnLoaded;
    }

    /// <summary>工具标识（对应配置 tools[].id），须在加入可视化树之前设置。</summary>
    public string ToolId { get; set; } = "";

    /// <summary>行标题。</summary>
    public string Title
    {
        get => _card.Title;
        set => _card.Title = value;
    }

    /// <summary>行说明。</summary>
    public string Description
    {
        get => _card.Description;
        set => _card.Description = value;
    }

    /// <summary>左侧图标字形。</summary>
    public string Glyph
    {
        get => _card.Glyph;
        set => _card.Glyph = value;
    }

    /// <summary>「立即执行」按钮的文案。构造后才由 XAML 赋值，因此必须即时写进控件。</summary>
    public string RunButtonText
    {
        get => _runButtonText;
        set
        {
            _runButtonText = value;
            _run.Content = value;
        }
    }

    /// <summary>「立即执行」完成后触发，供宿主刷新页面上的状态（如当前模式）。</summary>
    public event EventHandler? Executed;

    private static AppHost? Host => App.Host;

    private void Build()
    {
        _run.Content = _runButtonText;

        var trailing = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center,
        };
        trailing.Children.Add(_box);
        trailing.Children.Add(_enabled);
        trailing.Children.Add(_run);
        _card.Trailing = trailing;

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(_card);
        content.Children.Add(_info);
        Content = content;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Host is null || string.IsNullOrEmpty(ToolId))
            return;

        _binding = Host.Config.Tools.FirstOrDefault(t => t.Id == ToolId);
        _box.HotkeyText = _binding?.Hotkey;
        _enabled.IsOn = _binding?.Enabled ?? true;

        AutomationProperties.SetAutomationId(_box, $"HotkeyBox_{ToolId}");
        AutomationProperties.SetAutomationId(_enabled, $"HotkeyEnabled_{ToolId}");
        AutomationProperties.SetAutomationId(_run, $"HotkeyRunNow_{ToolId}");
        AutomationProperties.SetAutomationId(_info, $"HotkeyInfo_{ToolId}");

        _box.HotkeyChanged += (_, value) =>
        {
            if (_binding is null)
                return;

            _binding.Hotkey = value;
            Host.SaveConfig();
            ReportFailures(Host.ReloadBindings());
        };

        _enabled.Toggled += (_, _) =>
        {
            if (_binding is null)
                return;

            _binding.Enabled = _enabled.IsOn;
            Host.SaveConfig();
            ReportFailures(Host.ReloadBindings());
        };

        _run.Click += (_, _) => RunNow();

        ReportFailures(Host.ReloadBindings());
    }

    private void RunNow()
    {
        try
        {
            var router = new HotkeyRouter((MainWindow)App.MainWindow!, Host!.Config);
            ActionResult? result = router.Invoke(ToolId);

            _info.Severity = InfoBarSeverity.Success;
            _info.Title = result?.Message ?? $"已打开{Title}";
            _info.IsOpen = true;
            Executed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _info.Severity = InfoBarSeverity.Error;
            _info.Title = $"执行失败：{ex.Message}";
            _info.IsOpen = true;
        }
    }

    /// <summary>把与本行工具相关的热重载失败原因显示出来；没有失败则收起提示。</summary>
    private void ReportFailures(IReadOnlyList<string> failures)
    {
        string? failure = failures.FirstOrDefault(f => f.StartsWith(ToolId + "：", StringComparison.Ordinal));
        if (failure is null)
        {
            if (_info.Severity != InfoBarSeverity.Success)
                _info.IsOpen = false;
            return;
        }

        _info.Severity = InfoBarSeverity.Warning;
        _info.Title = failure;
        _info.IsOpen = true;
    }
}
