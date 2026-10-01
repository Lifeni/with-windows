using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;
using Windows.UI.Core;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Controls;

/// <summary>
/// 录制式热键输入框：聚焦后按下组合键即捕获（Ctrl+Alt+Shift+Win+键，支持 F1–F24、字母、数字）。
/// Esc 取消本次录制；"清除" 或 Backspace/Delete 清空。显示用 <see cref="HotkeyFormatter"/> 格式化。
/// </summary>
public sealed partial class HotkeyInputBox : UserControl
{
    private bool _recording;

    public static readonly DependencyProperty HotkeyTextProperty = DependencyProperty.Register(
        nameof(HotkeyText), typeof(string), typeof(HotkeyInputBox),
        new PropertyMetadata("", (d, _) => ((HotkeyInputBox)d).OnHotkeyTextChanged()));

    /// <summary>热键字符串（"Ctrl+Shift+F14" 或空）。双向绑定。</summary>
    public string HotkeyText
    {
        get => (string)GetValue(HotkeyTextProperty);
        set => SetValue(HotkeyTextProperty, value);
    }

    /// <summary>聚焦内部输入框进入录制模式（弹窗打开后调用）。</summary>
    public void FocusInput() => InputBox.Focus(FocusState.Programmatic);

    public HotkeyInputBox()
    {
        InitializeComponent();
        InputBox.GotFocus += (_, _) => StartRecording();
        InputBox.LostFocus += (_, _) => StopRecording();
        InputBox.KeyDown += OnKeyDown;
        ClearButton.Click += (_, _) => HotkeyText = "";
    }

    private void OnHotkeyTextChanged() => InputBox.Text = HotkeyText;

    private void StartRecording()
    {
        _recording = true;
        InputBox.Text = "按下组合键…";
    }

    private void StopRecording()
    {
        _recording = false;
        InputBox.Text = HotkeyText;
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;

        // Esc 取消；Backspace/Delete 清空
        if (e.Key == VirtualKey.Escape || e.Key == VirtualKey.Back || e.Key == VirtualKey.Delete)
        {
            if (e.Key == VirtualKey.Back || e.Key == VirtualKey.Delete)
                HotkeyText = "";
            InputBox.Focus(FocusState.Unfocused);
            return;
        }

        if (IsModifier(e.Key)) return; // 等待主键

        uint vk = (uint)e.Key;
        if (!IsSupportedKey(vk)) return;

        uint modifiers = CurrentModifiers();
        // 单个字母/数字不带修饰键会全局独占该键（比如录到 A 之后全系统按 A 都触发动作），必须拦下
        if (modifiers == 0 && !IsFunctionKey(vk))
        {
            InputBox.Text = "需配合 Ctrl / Alt / Shift / Win";
            return; // 保持录制状态，等待用户重新按键
        }

        HotkeyText = HotkeyFormatter.Format(new Hotkey(modifiers, vk));
        InputBox.Focus(FocusState.Unfocused);
    }

    private static bool IsModifier(VirtualKey key) => key is VirtualKey.Control or VirtualKey.Menu
        or VirtualKey.Shift or VirtualKey.LeftWindows or VirtualKey.RightWindows;

    private static bool IsSupportedKey(uint vk)
        => (vk >= 0x70 && vk <= 0x87)   // F1–F24
        || (vk >= (uint)'A' && vk <= (uint)'Z')
        || (vk >= (uint)'0' && vk <= (uint)'9');

    private static bool IsFunctionKey(uint vk) => vk is >= 0x70 and <= 0x87;

    private static uint CurrentModifiers()
    {
        uint mods = 0;
        if (IsDown(VirtualKey.Control)) mods |= NativeMethods.MOD_CONTROL;
        if (IsDown(VirtualKey.Menu)) mods |= NativeMethods.MOD_ALT;
        if (IsDown(VirtualKey.Shift)) mods |= NativeMethods.MOD_SHIFT;
        if (IsDown(VirtualKey.LeftWindows) || IsDown(VirtualKey.RightWindows)) mods |= NativeMethods.MOD_WIN;
        return mods;
    }

    private static bool IsDown(VirtualKey key)
        => InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);
}
