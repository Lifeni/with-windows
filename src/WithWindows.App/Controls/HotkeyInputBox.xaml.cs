using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using WithWindows.Core.Hotkeys;

namespace WithWindows.Controls;

/// <summary>
/// 录入式热键框：获得焦点后按下组合键即捕获（Esc 取消），另有清除按钮。
/// 校验复用核心层 <see cref="HotkeyParser"/>，保证界面与注册层规则一致。
/// </summary>
public sealed partial class HotkeyInputBox : UserControl
{
    private bool _capturing;
    private string? _hotkeyText;

    public HotkeyInputBox()
    {
        InitializeComponent();

        // UserControl 自身没有自动化节点：把页面设置的 AutomationId 转交到可聚焦的 BoxButton，
        // 否则录制框在可访问性树里既定位不到、也无法被键盘聚焦。
        // 清除按钮的 ID 必须按实例派生——一页里有多个录制框，写死会出现重复 ID。
        Loaded += (_, _) =>
        {
            string id = AutomationProperties.GetAutomationId(this);
            if (!string.IsNullOrEmpty(id))
            {
                AutomationProperties.SetAutomationId(BoxButton, id);
                AutomationProperties.SetAutomationId(ClearButton, id + "Clear");
            }
        };
    }

    /// <summary>热键文本（规范形式，如 "Ctrl+Shift+F14"）；null 或空表示不绑定。</summary>
    public string? HotkeyText
    {
        get => _hotkeyText;
        set
        {
            _hotkeyText = string.IsNullOrWhiteSpace(value) ? null : value;
            RefreshDisplay();
        }
    }

    /// <summary>用户完成一次录入（含清除）后触发；参数为新的热键文本或 null。</summary>
    public event EventHandler<string?>? HotkeyChanged;

    private void OnGettingFocus(object sender, GettingFocusEventArgs e)
    {
        _capturing = true;
        HideHint();
        RefreshDisplay();
    }

    /// <summary>
    /// 点击录制框重新进入录制状态。必须有这个入口：完成一次录入后焦点仍在按钮上，
    /// 再点一次不会触发 GettingFocus，否则用户无法重新录入（只能先让焦点离开再回来）。
    /// </summary>
    private void OnBoxClick(object sender, RoutedEventArgs e)
    {
        if (_capturing)
            return;

        _capturing = true;
        HideHint();
        RefreshDisplay();
    }

    private void OnLosingFocus(object sender, LosingFocusEventArgs e)
    {
        _capturing = false;
        RefreshDisplay();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        _capturing = false;
        _hotkeyText = null;
        RefreshDisplay();
        HotkeyChanged?.Invoke(this, null);
    }

    private void OnPreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_capturing)
            return;

        // 捕获期间吞掉按键，避免触发页面导航等默认行为
        e.Handled = true;

        if (e.Key == VirtualKey.Escape)
        {
            // Esc 取消本次录入，保留原值
            _capturing = false;
            HideHint();
            RefreshDisplay();
            BoxButton.Focus(FocusState.Programmatic);
            return;
        }

        if (IsModifierKey(e.Key))
        {
            ShowHint("继续按下一个键完成组合（Esc 取消）");
            return;
        }

        uint virtualKey = (uint)e.Key;
        HotkeyModifiers modifiers = CurrentModifiers();

        if (!HotkeyParser.IsFunctionKey(virtualKey) && !IsLetterOrDigit(virtualKey))
        {
            ShowHint("不支持的键（支持 F1–F24、字母、数字）");
            return;
        }

        // 字母/数字不带修饰键会全局独占该键，与解析层保持同一规则
        if (modifiers == HotkeyModifiers.None && !HotkeyParser.IsFunctionKey(virtualKey))
        {
            ShowHint("需要至少一个修饰键（Ctrl / Alt / Shift / Win），或使用 F1–F24");
            return;
        }

        var hotkey = new Hotkey(modifiers, virtualKey);
        _hotkeyText = HotkeyFormatter.Format(hotkey);
        _capturing = false;
        HideHint();
        RefreshDisplay();
        HotkeyChanged?.Invoke(this, _hotkeyText);
    }

    private void RefreshDisplay()
        => ValueText.Text = _capturing
            ? "请按下组合键…"
            : _hotkeyText ?? "未设置";

    private void ShowHint(string message)
    {
        HintText.Text = message;
        HintText.Visibility = Visibility.Visible;
    }

    private void HideHint()
    {
        HintText.Text = "";
        HintText.Visibility = Visibility.Collapsed;
    }

    private static bool IsModifierKey(VirtualKey key)
        => key is VirtualKey.Control or VirtualKey.Menu or VirtualKey.Shift
            or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static bool IsLetterOrDigit(uint virtualKey)
        => (virtualKey >= 'A' && virtualKey <= 'Z') || (virtualKey >= '0' && virtualKey <= '9');

    private static HotkeyModifiers CurrentModifiers()
    {
        HotkeyModifiers modifiers = HotkeyModifiers.None;
        if (IsDown(VirtualKey.Control)) modifiers |= HotkeyModifiers.Control;
        if (IsDown(VirtualKey.Menu)) modifiers |= HotkeyModifiers.Alt;
        if (IsDown(VirtualKey.Shift)) modifiers |= HotkeyModifiers.Shift;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) modifiers |= HotkeyModifiers.Windows;
        return modifiers;
    }

    private static bool IsDown(VirtualKey key)
        => (InputKeyboardSource.GetKeyStateForCurrentThread(key) & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
}
