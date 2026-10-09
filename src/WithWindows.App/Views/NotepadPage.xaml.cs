using System;
using System.Linq;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.UI.Core;
using WithWindows.Core.Config;
using WithWindows.Services;

// Microsoft.UI.Dispatching 与 Windows.System 都有 DispatcherQueueTimer，这里明确用 WinUI 的那个
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace WithWindows.Views;

/// <summary>
/// 记事页：顶部工具条（查找替换 / 另存为 + 秒级时钟）+ 占满整页的编辑器 + 底部状态栏（行列 / 字符数 / 自动换行 / 缩放）。
/// 不做格式化按钮组（加粗 / 斜体等用编辑器原生快捷键）。草稿按防抖自动写盘，复制粘贴一律手动——**关闭程序不自动复制到剪贴板**。
/// 字号持久化在 <see cref="WindowStateConfig.NotepadFontSize"/>。
/// </summary>
public sealed partial class NotepadPage : Page
{
    /// <summary>导航项 Tag 常量，供 MainWindow 路由表引用。</summary>
    public static string RouteTag => "notepad";

    /// <summary>字号范围，与设计文档一致。</summary>
    private const double MinFontSize = 10;
    private const double MaxFontSize = 32;

    /// <summary>重置缩放时回到的字号。</summary>
    private const double DefaultFontSize = 14;

    private NotepadDraft? _draft;
    private DispatcherQueueTimer? _clock;
    private bool _loading;

    // 当前行距（切换字号时保持）
    private float _lastLineSpacing = 1.05f;

    /// <summary>
    /// 载入完成后编辑器内的实际文本，用作「是否真的被编辑过」的基线。
    /// RichEditBox 取回文本时会规范化行尾，若直接与文件内容比较会判定为「已编辑」，
    /// 于是仅仅打开页面就把规范化结果写回用户草稿（空行逐次累积）。
    /// </summary>
    private string _editorBaseline = "";

    public NotepadPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

        // RichEditBox 的模板内部有 ScrollViewer，内容可滚动时可能把滚轮事件标记为已处理；
        // 用 handledEventsToo 保证 Ctrl+滚轮 缩放始终收得到（与 HotkeyInputBox 的指针事件同源问题）。
        Editor.AddHandler(
            UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(OnEditorPointerWheel),
            handledEventsToo: true);
    }

    private static AppConfig? Config => App.Host?.Config;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            LoadCore();
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"记事页加载失败：{ex}");
        }
    }

    private void LoadCore()
    {
        if (_draft is null)
        {
            _loading = true;
            try
            {
                Editor.Document.SetText(TextSetOptions.None, NotepadDraft.Load());
                ApplyLineSpacing();
                ApplyFontSize(Config?.WindowState.NotepadFontSize ?? DefaultFontSize);

                // 取载入后的规范化文本作为基线，之后只有与它不同才认为用户真的改了内容
                Editor.Document.GetText(TextGetOptions.None, out string baseline);
                _editorBaseline = baseline;
            }
            finally
            {
                _loading = false;
            }

            _draft = new NotepadDraft(DispatcherQueue);
        }

        Editor.TextWrapping = WrapToggle.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;
        StartClock();
        UpdateStatus();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _clock?.Stop();
        _draft?.Flush();
    }

    /// <summary>工具条左侧的秒级时钟；页面离开时停表。</summary>
    private void StartClock()
    {
        if (_clock is null)
        {
            _clock = DispatcherQueue.CreateTimer();
            _clock.Interval = TimeSpan.FromSeconds(1);
            _clock.IsRepeating = true;
            _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        }

        ClockText.Text = DateTime.Now.ToString("HH:mm:ss");
        _clock.Start();
    }

    /// <summary>
    /// 行距 1.05（设计文档的可读性要求）。设置默认段落格式，并对已有内容再应用一次——
    /// 载入文本会把段落格式重置为默认，只设默认值对已有内容不生效。
    /// </summary>
    private void ApplyLineSpacing(float lineSpacing = 1.05f)
    {
        var format = Editor.Document.GetDefaultParagraphFormat();
        format.SetLineSpacing(LineSpacingRule.Multiple, lineSpacing);
        Editor.Document.SetDefaultParagraphFormat(format);

        var selection = Editor.Document.Selection;
        selection.SetRange(0, int.MaxValue);
        selection.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, lineSpacing);
        selection.SetRange(0, 0);

        _lastLineSpacing = lineSpacing;
    }

    private void OnEditorTextChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
            return;

        Editor.Document.GetText(TextGetOptions.None, out string text);
        UpdateCount(text);
        UpdateCaret();

        // 未真正编辑（只是富文本往返规范化了行尾）时不写回磁盘
        if (string.Equals(text, _editorBaseline, StringComparison.Ordinal))
            return;

        _editorBaseline = text;
        _draft?.QueueSave(text);
    }

    private void OnEditorSelectionChanged(object sender, RoutedEventArgs e) => UpdateCaret();

    /// <summary>刷新状态栏全部字段（仅在载入完成时用一次）。</summary>
    private void UpdateStatus()
    {
        Editor.Document.GetText(TextGetOptions.None, out string text);
        UpdateCount(text);
        UpdateCaret();
        UpdateZoom();
    }

    /// <summary>字符数：只在文本变化时计算，避免每次移动光标都整篇取文。</summary>
    private void UpdateCount(string text)
    {
        string content = text.TrimEnd('\r');   // 富文本末尾总有一个段落标记，不计入字符数
        StatusCount.Text = $"{content.Length} 个字符";
    }

    /// <summary>缩放比例。</summary>
    private void UpdateZoom()
    {
        StatusZoom.Text = $"缩放 {Math.Round(Editor.FontSize / DefaultFontSize * 100)}%";
    }

    /// <summary>光标行列：只取光标前的前缀（移动光标时不再整篇取文）。</summary>
    private void UpdateCaret()
    {
        int start = Editor.Document.Selection.StartPosition;
        Editor.Document.GetRange(0, start).GetText(TextGetOptions.None, out string before);
        int line = before.Count(c => c == '\r') + 1;
        int lastBreak = before.LastIndexOf('\r');
        int column = start - lastBreak;
        StatusCaret.Text = $"行 {line}，列 {column}";
    }

    private void OnFindAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowFindPanel(expandReplace: false);
    }

    private void OnReplaceAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ShowFindPanel(expandReplace: true);
    }

    private void OnSaveAsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OnSaveAsClick(this, new RoutedEventArgs());
    }

    private void OnFindClick(object sender, RoutedEventArgs e) => ShowFindPanel(expandReplace: false);

    /// <summary>显示查找面板；展开替换时把焦点直接放到替换框。</summary>
    private void ShowFindPanel(bool expandReplace)
    {
        FindPanel.Visibility = Visibility.Visible;
        SetReplaceRowExpanded(expandReplace);

        if (expandReplace)
            ReplaceBox.Focus(FocusState.Programmatic);
        else
            FindBox.Focus(FocusState.Programmatic);
    }

    /// <summary>左侧箭头：展开 / 收起替换行（对齐系统记事本：默认只有查找行）。</summary>
    private void OnFindExpandClick(object sender, RoutedEventArgs e)
        => SetReplaceRowExpanded(ReplaceRow.Visibility != Visibility.Visible);

    private void SetReplaceRowExpanded(bool expanded)
    {
        ReplaceRow.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
        // 与系统记事本一致：折叠时箭头朝上、展开后朝下
        FindExpandIcon.Glyph = expanded ? "\uE70D" : "\uE70E";

        string text = expanded ? "收起替换" : "展开替换";
        AutomationProperties.SetName(FindExpandButton, text);
        ToolTipService.SetToolTip(FindExpandButton, text);

        if (expanded)
            ReplaceBox.Focus(FocusState.Programmatic);
    }

    private void OnFindCloseClick(object sender, RoutedEventArgs e)
    {
        FindPanel.Visibility = Visibility.Collapsed;
        Editor.Focus(FocusState.Programmatic);
    }

    private void OnFindBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        FindNext(forward: true);
    }

    private void OnReplaceBoxKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        e.Handled = true;
        ReplaceCurrent();
    }

    /// <summary>改了查找词就把「未命中」的标红清掉，避免旧状态留在界面上。</summary>
    private void OnFindBoxTextChanged(object sender, TextChangedEventArgs e) => ClearFindMiss();

    private void OnFindNextClick(object sender, RoutedEventArgs e) => FindNext(forward: true);

    private void OnFindPrevClick(object sender, RoutedEventArgs e) => FindNext(forward: false);

    private bool MatchCase => FindMatchCase.IsChecked == true;

    private StringComparison Comparison => MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>查找：向后从光标处找、向前从光标前找，都会回绕。</summary>
    private void FindNext(bool forward)
    {
        string needle = FindBox.Text;
        if (string.IsNullOrEmpty(needle))
            return;

        string text = ReadAllText();
        int caretStart = Math.Clamp(Editor.Document.Selection.StartPosition, 0, text.Length);
        int caretEnd = Math.Clamp(Editor.Document.Selection.EndPosition, 0, text.Length);

        int index = forward
            ? text.IndexOf(needle, caretEnd, Comparison)
            : text.LastIndexOf(needle, Math.Max(0, caretStart - 1), Comparison);

        if (index < 0)
        {
            index = forward
                ? text.IndexOf(needle, 0, Comparison)
                : text.LastIndexOf(needle, text.Length - 1, Comparison);
        }

        if (index < 0)
        {
            MarkFindMiss();
            return;
        }

        ClearFindMiss();
        SelectMatch(index, needle.Length);
    }

    /// <summary>
    /// 选中命中项并滚进可视区。高亮靠 RichEditBox 的
    /// <c>SelectionHighlightColorWhenNotFocused</c>（见 XAML）——焦点在查找框上时选区照样可见，
    /// 这正是「用选区当高亮」的前提；RichEdit 不支持字符背景色，别去走 ITextCharacterFormat.BackgroundColor。
    /// </summary>
    private void SelectMatch(int index, int length)
    {
        ITextRange match = Editor.Document.Selection;
        match.SetRange(index, index + length);
        ScrollToSelection(match);
    }

    /// <summary>
    /// 没命中时把查找框描边标红（对齐系统记事本：有反馈但不额外占一行文案）。
    /// 结果文本行已按用户要求去掉，没有这个提示时「找不到」和「没反应」分不出来。
    /// </summary>
    private void MarkFindMiss()
    {
        FindBox.BorderBrush = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
        FindBox.BorderThickness = new Thickness(0, 0, 0, 2);
    }

    private void ClearFindMiss()
    {
        if (FindBox.ReadLocalValue(TextBox.BorderBrushProperty) == DependencyProperty.UnsetValue)
            return;

        FindBox.ClearValue(TextBox.BorderBrushProperty);
        FindBox.ClearValue(TextBox.BorderThicknessProperty);
    }

    /// <summary>
    /// 把命中位置滚进可视区。RichEditBox 在**没有键盘焦点**时不会自己跟随选区滚动，
    /// 而查找时焦点一直在查找框上——不显式调用就会「找到了但看不到」，像是没跳转。
    /// </summary>
    private static void ScrollToSelection(ITextRange range)
    {
        try
        {
            range.ScrollIntoView(PointOptions.None);
        }
        catch (Exception ex)
        {
            // 定位失败不该影响查找本身（选区已经设好），记日志即可
            App.Host?.Log.Error($"滚动到命中位置失败：{ex.Message}");
        }
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e) => ReplaceCurrent();

    /// <summary>替换当前选中的匹配项；没选中就当作「查找下一个」。</summary>
    private void ReplaceCurrent()
    {
        string needle = FindBox.Text;
        if (string.IsNullOrEmpty(needle))
            return;

        Editor.Document.Selection.GetText(TextGetOptions.None, out string selected);
        if (string.Equals(selected.TrimEnd('\r'), needle, Comparison))
            Editor.Document.Selection.SetText(TextSetOptions.None, ReplaceBox.Text);

        FindNext(forward: true);
    }

    private void OnReplaceAllClick(object sender, RoutedEventArgs e)
    {
        string needle = FindBox.Text;
        if (string.IsNullOrEmpty(needle))
            return;

        string text = ReadAllText();
        string replacement = ReplaceBox.Text;
        var builder = new System.Text.StringBuilder();
        int cursor = 0;
        int index = text.IndexOf(needle, 0, Comparison);
        while (index >= 0)
        {
            builder.Append(text, cursor, index - cursor);
            builder.Append(replacement);
            cursor = index + needle.Length;
            index = text.IndexOf(needle, cursor, Comparison);
        }
        builder.Append(text, cursor, text.Length - cursor);

        Editor.Document.SetText(TextSetOptions.None, builder.ToString());
        UpdateStatus();
    }

    /// <summary>
    /// 另存为：正文本身自动保存（防抖写草稿），这个按钮用于把内容另存到用户选定的文件。
    /// </summary>
    private async void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileSavePicker();
            picker.FileTypeChoices.Add("文本文件", new List<string> { ".txt" });
            picker.SuggestedFileName = "记事";

            // 未打包应用必须先把选择器与窗口关联，否则 PickSaveFileAsync 会失败
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow!));

            StorageFile? file = await picker.PickSaveFileAsync();
            if (file is null)
                return;

            string content = ReadAllText();
            await FileIO.WriteTextAsync(file, content);
            StatusCount.Text = $"{content.Length} 个字符（已另存为 {file.Name}）";
        }
        catch (Exception ex)
        {
            StatusCount.Text = $"另存为失败：{ex.Message}";
        }
    }

    private void OnWrapToggled(object sender, RoutedEventArgs e)
    {
        // XAML 里 IsChecked="True" 会在解析期就触发 Checked，此时后面的 Editor 还没构造出来
        if (Editor is null)
            return;

        Editor.TextWrapping = WrapToggle.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;

        // 未开启时不显示对勾，但保留占位（用透明而不是折叠），避免文字位置跳变。
        // 控件在解析期就会被调用，此时 WrapCheck 可能尚未构造。
        if (WrapCheck is not null)
            WrapCheck.Opacity = WrapToggle.IsChecked == true ? 1 : 0;
    }

    private string ReadAllText()
    {
        Editor.Document.GetText(TextGetOptions.None, out string text);
        return text.TrimEnd('\r');
    }

    private void OnEditorPointerWheel(object sender, PointerRoutedEventArgs e)
    {
        // 只在按住 Ctrl 时缩放，普通滚动保持默认行为
        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        if (!ctrl)
            return;

        int delta = e.GetCurrentPoint(Editor).Properties.MouseWheelDelta;
        ApplyFontSize(Editor.FontSize + (delta > 0 ? 1 : -1), persist: true);
        e.Handled = true;
    }

    private void ApplyFontSize(double size, bool persist = false)
    {
        double clamped = Math.Clamp(size, MinFontSize, MaxFontSize);
        Editor.FontSize = clamped;
        UpdateZoom();

        if (!persist || Config is null)
            return;

        Config.WindowState.NotepadFontSize = clamped;
        App.Host?.SaveConfig();
    }
}
