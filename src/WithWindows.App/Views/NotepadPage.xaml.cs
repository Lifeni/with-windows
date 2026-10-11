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

    /// <summary>聚焦重试轮数：窗口尚未激活时 Focus 不生效，需要等焦点真正落下再校准光标。</summary>
    private const int FocusRetryLimit = 5;

    /// <summary>重置缩放时回到的字号。</summary>
    private const double DefaultFontSize = 14;

    private NotepadDraft? _draft;
    private DispatcherQueueTimer? _clock;
    private bool _loading;

    /// <summary>监听激活的窗口；页面离开时解除订阅，避免窗口反过来持有已废弃的页面。</summary>
    private Window? _focusWindow;

    /// <summary>本页加载后是否已校准过光标位置；见 <see cref="FocusEditor"/>。</summary>
    private bool _caretPlaced;

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

        // Ctrl+F / Ctrl+H / Ctrl+S：见 XAML 顶部注释——用 KeyDown 而不是 KeyboardAccelerator
        AddHandler(
            UIElement.KeyDownEvent,
            new KeyEventHandler(OnPageKeyDown),
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

        // 切到本页就把焦点交给编辑器，用户可以直接开始输入。
        // 启动时本页在窗口激活之前就加载完，那时 Focus 会被紧随其后的激活流程覆盖，
        // 所以再挂一次窗口 Activated，等窗口真正拿到焦点后补一次。
        if (_focusWindow is null && App.MainWindow is { } window)
        {
            _focusWindow = window;
            _focusWindow.Activated += OnWindowActivated;
        }

        FocusEditor();
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
        if (_focusWindow is not null)
        {
            _focusWindow.Activated -= OnWindowActivated;
            _focusWindow = null;
        }

        _clock?.Stop();
        _draft?.Flush();
    }

    /// <summary>
    /// 窗口重新激活且本页仍在时把焦点交还编辑器（「切到记事就能直接打字」的那一步）。
    /// 参数类型必须写全限定名：Windows.UI.Core 里也有同名的 WindowActivatedEventArgs（本文件为取
    /// CoreVirtualKeyStates 引了该命名空间）。
    /// </summary>
    private void OnWindowActivated(object sender, Microsoft.UI.Xaml.WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState != WindowActivationState.Deactivated)
            FocusEditor();
    }

    /// <summary>
    /// 把键盘焦点移到编辑器。Loaded 阶段页面刚进可视树，此时直接调用 Focus 会被紧随其后的布局与窗口激活覆盖，
    /// 因此排到队列末尾执行；焦点还没落下时再排一轮重试（最多 <see cref="FocusRetryLimit"/> 轮）。
    /// </summary>
    private void FocusEditor(int attemptsLeft = FocusRetryLimit)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!IsLoaded)
                return;

            Editor.Focus(FocusState.Programmatic);

            if (_caretPlaced)
                return;

            // 焦点确实落在编辑器上之后再校准光标：RichEditBox 首次获得焦点会把光标放到文末段落标记之后，
            // 不校准的话空记事会显示「行 2，列 1」（多算了那个段落标记）。
            if (!ReferenceEquals(FocusManager.GetFocusedElement(XamlRoot), Editor))
            {
                if (attemptsLeft > 0)
                    FocusEditor(attemptsLeft - 1);
                return;
            }

            _caretPlaced = true;
            Editor.Document.GetText(TextGetOptions.None, out string text);
            int end = text.TrimEnd('\r').Length;   // 与字符数同一口径：结尾段落标记不算正文
            Editor.Document.Selection.SetRange(end, end);
        });
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

    /// <summary>
    /// 页面级快捷键：Ctrl+F 查找、Ctrl+H 替换（顺带展开替换行）、Ctrl+S 另存为。
    /// 用 KeyDown + handledEventsToo（编辑框会先吃掉一部分按键，必须带 handledEventsToo）；
    /// 不用 KeyboardAccelerator 的原因见 XAML 顶部注释（它会弹「Ctrl+F」悬停提示）。
    /// </summary>
    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key is not (VirtualKey.F or VirtualKey.H or VirtualKey.S))
            return;

        bool ctrl = (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        if (!ctrl)
            return;

        e.Handled = true;
        switch (e.Key)
        {
            case VirtualKey.F:
                Breadcrumb("快捷键 Ctrl+F");
                ShowFindPanel(expandReplace: false);
                break;
            case VirtualKey.H:
                Breadcrumb("快捷键 Ctrl+H");
                ShowFindPanel(expandReplace: true);
                break;
            default:
                OnSaveAsClick(this, new RoutedEventArgs());
                break;
        }
    }

    private void OnFindClick(object sender, RoutedEventArgs e) => ShowFindPanel(expandReplace: false);

    /// <summary>显示查找面板；展开替换时把焦点直接放到替换框。</summary>
    private void ShowFindPanel(bool expandReplace)
    {
        Breadcrumb(expandReplace ? "打开面板（展开替换）" : "打开面板");
        FindPanel.Visibility = Visibility.Visible;
        SetReplaceRowExpanded(expandReplace);

        if (expandReplace)
            ReplaceBox.Focus(FocusState.Programmatic);
        else
            FindBox.Focus(FocusState.Programmatic);
    }

    /// <summary>左侧箭头：展开 / 收起替换行（对齐系统记事本：默认只有查找行）。</summary>
    private void OnFindExpandClick(object sender, RoutedEventArgs e)
    {
        bool expanded = ReplaceRow.Visibility != Visibility.Visible;
        Breadcrumb(expanded ? "展开替换行" : "收起替换行");
        SetReplaceRowExpanded(expanded);
    }

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
        Breadcrumb("关闭面板");
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
            Breadcrumb($"查找「{needle}」未命中");
            MarkFindMiss();
            return;
        }

        Breadcrumb($"查找「{needle}」命中第 {index + 1} 个字符");
        ClearFindMiss();
        SelectMatch(index, needle.Length);
    }

    /// <summary>
    /// 「最后动作」面包屑：崩溃是原生层的（0xC000027B 不经过 App.UnhandledException），
    /// 只能靠崩溃前最后一条日志判断用户点了什么。定位完可以按需删掉。
    /// </summary>
    private static void Breadcrumb(string action) => App.Host?.Log.Info($"记事：{action}");

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
        Breadcrumb("替换当前项");
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
        Breadcrumb("全部替换");
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
        Breadcrumb("另存为");
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

        Breadcrumb(WrapToggle.IsChecked == true ? "开启自动换行" : "关闭自动换行");
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
