using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace WithWindows.Controls;

/// <summary>快捷键编辑对话框的返回值。<see cref="Confirmed"/> 为 false 表示用户取消。</summary>
/// <param name="Confirmed">是否点击了「保存」。</param>
/// <param name="Hotkey">新的热键文本；null 表示已清除绑定。</param>
public readonly record struct HotkeyDialogResult(bool Confirmed, string? Hotkey);

/// <summary>
/// 快捷键编辑对话框：与 PowerToys 一致的形态——说明文字 + 录制区 + 「重置 / 清除」+「保存 / 取消」。
/// 录制本身复用 <see cref="HotkeyInputBox"/>（聚焦后按组合键、Esc 取消、实时校验）。
/// </summary>
public static class HotkeyDialog
{
    public static async Task<HotkeyDialogResult> ShowAsync(XamlRoot xamlRoot, string title, string? current)
    {
        var capture = new HotkeyInputBox
        {
            HotkeyText = current,
            MinWidth = 320,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetAutomationId(capture, "HotkeyDialogCapture");

        var reset = new HyperlinkButton { Content = "重置" };
        AutomationProperties.SetAutomationId(reset, "HotkeyDialogReset");
        reset.Click += (_, _) => capture.HotkeyText = current;

        var clear = new HyperlinkButton { Content = "清除" };
        AutomationProperties.SetAutomationId(clear, "HotkeyDialogClear");
        clear.Click += (_, _) => capture.HotkeyText = null;

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        actions.Children.Add(reset);
        actions.Children.Add(clear);

        var content = new StackPanel { Spacing = 12, MinWidth = 340 };
        content.Children.Add(new TextBlock
        {
            Text = "快捷方式应以 Ctrl、Alt、Shift 或 Win 开头。",
            TextWrapping = TextWrapping.Wrap,
        });
        content.Children.Add(capture);
        content.Children.Add(actions);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = title,
            Content = content,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        ContentDialogResult result = await dialog.ShowAsync();
        return new HotkeyDialogResult(result == ContentDialogResult.Primary, capture.HotkeyText);
    }
}
