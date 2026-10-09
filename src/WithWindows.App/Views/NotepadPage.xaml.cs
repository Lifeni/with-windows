using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.System;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Core;
using WithWindows.Core.Config;
using WithWindows.Services;

// Microsoft.UI.Dispatching 与 Windows.System 都有 DispatcherQueueTimer，这里明确用 WinUI 的那个
using DispatcherQueueTimer = Microsoft.UI.Dispatching.DispatcherQueueTimer;

namespace WithWindows.Views;

/// <summary>
/// 记事页：顶部工具条（保存 / 复制 / 粘贴 / 自动换行 / 缩放）+ 占满整页的编辑器 + 底部状态栏（行列 / 字符数 / 缩放）。
/// 纯文本、不做格式化。草稿仍按防抖自动写盘，复制粘贴一律手动——**关闭程序不自动复制到剪贴板**。
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

    // 查找替换的上次输入与选项
    private string _lastFindText = "";
    private string _lastReplaceText = "";
    private bool _matchCase;

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

    // ---- Markdown 格式（作用于当前选区；无选区时作用于随后输入的内容）----

    private void OnBoldClick(object sender, RoutedEventArgs e)
        => Editor.Document.Selection.CharacterFormat.Bold = FormatEffect.Toggle;

    private void OnItalicClick(object sender, RoutedEventArgs e)
        => Editor.Document.Selection.CharacterFormat.Italic = FormatEffect.Toggle;

    private void OnStrikethroughClick(object sender, RoutedEventArgs e)
        => Editor.Document.Selection.CharacterFormat.Strikethrough = FormatEffect.Toggle;

    private void OnHeadingBodyClick(object sender, RoutedEventArgs e) => ApplyHeading(DefaultFontSize, bold: false);

    private void OnHeading1Click(object sender, RoutedEventArgs e) => ApplyHeading(28, bold: true);

    private void OnHeading2Click(object sender, RoutedEventArgs e) => ApplyHeading(24, bold: true);

    private void OnHeading3Click(object sender, RoutedEventArgs e) => ApplyHeading(20, bold: true);

    /// <summary>标题：设置段落的字号与粗细（RichEditBox 无内置标题样式，用字号 + 粗体表达）。</summary>
    private void ApplyHeading(double size, bool bold)
    {
        ITextCharacterFormat format = Editor.Document.Selection.CharacterFormat;
        format.Size = (float)size;
        format.Bold = bold ? FormatEffect.On : FormatEffect.Off;
    }

    private void OnListNoneClick(object sender, RoutedEventArgs e) => ApplyList(MarkerType.None);

    private void OnListBulletedClick(object sender, RoutedEventArgs e) => ApplyList(MarkerType.Bullet);

    private void OnListNumberedClick(object sender, RoutedEventArgs e) => ApplyList(MarkerType.Arabic);

    private void ApplyList(MarkerType markerType)
    {
        ITextParagraphFormat format = Editor.Document.Selection.ParagraphFormat;
        format.ListType = markerType;
        Editor.Document.Selection.ParagraphFormat = format;
    }

    /// <summary>插入链接：给选区设置链接地址；未选中内容时先提示（RichEditBox 需要选中文本才能加链接）。</summary>
    private async void OnLinkClick(object sender, RoutedEventArgs e)
    {
        Editor.Document.Selection.GetText(TextGetOptions.None, out string selected);
        if (string.IsNullOrEmpty(selected.TrimEnd('\r')))
        {
            StatusCount.Text = "请先选中要加链接的文字";
            return;
        }

        var urlBox = new TextBox { Header = "链接地址", PlaceholderText = "https://" };
        AutomationProperties.SetAutomationId(urlBox, "LinkUrlBox");

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "插入链接",
            Content = urlBox,
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        string url = urlBox.Text.Trim();
        if (url.Length == 0)
            return;

        var selection = Editor.Document.Selection;
        selection.Link = url;
    }

    /// <summary>清除格式：把选区的字符格式与段落格式恢复默认。</summary>
    private void OnClearFormatClick(object sender, RoutedEventArgs e)
    {
        var selection = Editor.Document.Selection;

        ITextCharacterFormat character = selection.CharacterFormat;
        character.Bold = FormatEffect.Off;
        character.Italic = FormatEffect.Off;
        character.Strikethrough = FormatEffect.Off;
        character.Underline = UnderlineType.None;
        character.Size = (float)DefaultFontSize;
        selection.CharacterFormat = character;

        ITextParagraphFormat paragraph = selection.ParagraphFormat;
        paragraph.ListType = MarkerType.None;
        selection.ParagraphFormat = paragraph;

        selection.Link = "";
    }

    private void OnFindAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        _ = ShowFindReplaceAsync();
    }

    private void OnSaveAsAccelerator(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OnSaveAsClick(this, new RoutedEventArgs());
    }

    private void OnFindClick(object sender, RoutedEventArgs e) => ShowFindPanel(focusReplace: false);

    private void ShowFindPanel(bool focusReplace)
    {
        FindPanel.Visibility = Visibility.Visible;
        FindPanel.Opacity = 1;

        if (focusReplace)
            ReplaceBox.Focus(FocusState.Programmatic);
        else
            FindBox.Focus(FocusState.Programmatic);
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

    private void OnFindNextClick(object sender, RoutedEventArgs e) => FindNext(forward: true);

    private void OnFindPrevClick(object sender, RoutedEventArgs e) => FindNext(forward: false);

    private bool MatchCase => FindMatchCase.IsChecked == true;

    private StringComparison Comparison => MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    /// <summary>查找：向后从光标处找、向前从光标前找，都会回绕。</summary>
    private void FindNext(bool forward)
    {
        string needle = FindBox.Text;
        if (string.IsNullOrEmpty(needle))
        {
            FindStatus.Text = "请输入查找内容";
            return;
        }

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
            FindStatus.Text = "没有找到匹配内容";
            return;
        }

        Editor.Document.Selection.SetRange(index, index + needle.Length);
        FindStatus.Text = $"已选中第 {index + 1} 个字符处";
    }

    private void OnReplaceClick(object sender, RoutedEventArgs e) => ReplaceCurrent();

    /// <summary>替换当前选中的匹配项；没选中就当作「查找下一个」。</summary>
    private void ReplaceCurrent()
    {
        string needle = FindBox.Text;
        if (string.IsNullOrEmpty(needle))
        {
            FindStatus.Text = "请输入查找内容";
            return;
        }

        Editor.Document.Selection.GetText(TextGetOptions.None, out string selected);
        if (string.Equals(selected.TrimEnd('\r'), needle, Comparison))
        {
            Editor.Document.Selection.SetText(TextSetOptions.None, ReplaceBox.Text);
            FindStatus.Text = "已替换";
        }
        else
        {
            FindStatus.Text = "当前未选中匹配内容，已为你查找下一个";
        }

        FindNext(forward: true);
    }

    private void OnReplaceAllClick(object sender, RoutedEventArgs e)
    {
        string needle = FindBox.Text;
        if (string.IsNullOrEmpty(needle))
        {
            FindStatus.Text = "请输入查找内容";
            return;
        }

        string text = ReadAllText();
        string replacement = ReplaceBox.Text;
        var builder = new System.Text.StringBuilder();
        int count = 0;
        int cursor = 0;
        int index = text.IndexOf(needle, 0, Comparison);
        while (index >= 0)
        {
            builder.Append(text, cursor, index - cursor);
            builder.Append(replacement);
            cursor = index + needle.Length;
            count++;
            index = text.IndexOf(needle, cursor, Comparison);
        }
        builder.Append(text, cursor, text.Length - cursor);

        Editor.Document.SetText(TextSetOptions.None, builder.ToString());
        UpdateStatus();
        FindStatus.Text = $"已替换 {count} 处";
    }

    /// <summary>查找替换对话框：纯文本查找，支持逐个替换与全部替换。</summary>
    private async Task ShowFindReplaceAsync()
    {
        var findBox = new TextBox { Header = "查找内容", Text = _lastFindText };
        var replaceBox = new TextBox { Header = "替换为", Text = _lastReplaceText };
        var caseCheck = new CheckBox { Content = "区分大小写", IsChecked = _matchCase };
        var status = new TextBlock
        {
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
        };

        AutomationProperties.SetAutomationId(findBox, "FindBox");
        AutomationProperties.SetAutomationId(replaceBox, "ReplaceBox");
        AutomationProperties.SetAutomationId(caseCheck, "FindMatchCase");
        AutomationProperties.SetAutomationId(status, "FindStatus");

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var findNext = new Button { Content = "查找下一个" };
        var replaceOne = new Button { Content = "替换" };
        var replaceAll = new Button { Content = "全部替换" };
        AutomationProperties.SetAutomationId(findNext, "FindNextButton");
        AutomationProperties.SetAutomationId(replaceOne, "ReplaceButton");
        AutomationProperties.SetAutomationId(replaceAll, "ReplaceAllButton");
        buttons.Children.Add(findNext);
        buttons.Children.Add(replaceOne);
        buttons.Children.Add(replaceAll);

        var content = new StackPanel { Spacing = 12, MinWidth = 360 };
        content.Children.Add(findBox);
        content.Children.Add(replaceBox);
        content.Children.Add(caseCheck);
        content.Children.Add(buttons);
        content.Children.Add(status);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "查找和替换",
            Content = content,
            CloseButtonText = "关闭",
        };

        bool MatchCase() => caseCheck.IsChecked == true;
        string Text() => ReadAllText();
        void Remember()
        {
            _lastFindText = findBox.Text;
            _lastReplaceText = replaceBox.Text;
            _matchCase = MatchCase();
        }
        findNext.Click += (_, _) =>
        {
            Remember();
            string needle = findBox.Text;
            if (string.IsNullOrEmpty(needle))
            {
                status.Text = "请输入查找内容";
                return;
            }

            string text = Text();
            var comparison = MatchCase() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int from = Math.Clamp(Editor.Document.Selection.EndPosition, 0, text.Length);

            // 先从光标向后找，找不到再回绕到开头
            int index = text.IndexOf(needle, from, comparison);
            if (index < 0)
                index = text.IndexOf(needle, 0, comparison);

            if (index < 0)
            {
                status.Text = "没有找到匹配内容";
                return;
            }

            Editor.Document.Selection.SetRange(index, index + needle.Length);
            Editor.Focus(FocusState.Programmatic);
            status.Text = $"已选中第 {index + 1} 个字符处";
        };

        replaceOne.Click += (_, _) =>
        {
            Remember();
            Editor.Document.Selection.GetText(TextGetOptions.None, out string selected);
            if (!string.IsNullOrEmpty(findBox.Text)
                && string.Equals(selected.TrimEnd('\r'), findBox.Text,
                    MatchCase() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase))
            {
                Editor.Document.Selection.SetText(TextSetOptions.None, replaceBox.Text);
            }

            status.Text = "已替换当前选中项（如未选中匹配内容则先点「查找下一个」）";
        };

        replaceAll.Click += (_, _) =>
        {
            Remember();
            if (string.IsNullOrEmpty(findBox.Text))
                return;

            string text = Text();
            var comparison = MatchCase() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            int count = 0;
            int index = text.IndexOf(findBox.Text, 0, comparison);
            var builder = new System.Text.StringBuilder();
            int cursor = 0;
            while (index >= 0)
            {
                builder.Append(text, cursor, index - cursor);
                builder.Append(replaceBox.Text);
                cursor = index + findBox.Text.Length;
                count++;
                index = text.IndexOf(findBox.Text, cursor, comparison);
            }
            builder.Append(text, cursor, text.Length - cursor);

            Editor.Document.SetText(TextSetOptions.None, builder.ToString());
            status.Text = $"已替换 {count} 处";
        };

        await dialog.ShowAsync();
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

    /// <summary>复制：有选区就复制选区，否则复制全文。</summary>
    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        Editor.Document.Selection.GetText(TextGetOptions.None, out string selected);
        string payload = string.IsNullOrEmpty(selected)
            ? ReadAllText()
            : selected.TrimEnd('\r');

        var package = new DataPackage();
        package.SetText(payload);
        Clipboard.SetContent(package);

        try
        {
            // 不 Flush 的话数据由本进程延迟提供：退出应用后剪贴板就失效了
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"剪贴板 Flush 失败：{ex.Message}");
        }
    }

    /// <summary>粘贴：把剪贴板文本插入到光标处（替换当前选区）。</summary>
    private async void OnPasteClick(object sender, RoutedEventArgs e)
    {
        try
        {
            DataPackageView view = Clipboard.GetContent();
            if (!view.Contains(StandardDataFormats.Text))
                return;

            string pasted = await view.GetTextAsync();
            Editor.Document.Selection.SetText(TextSetOptions.None, pasted);
            Editor.Document.Selection.SetRange(
                Editor.Document.Selection.EndPosition,
                Editor.Document.Selection.EndPosition);
            Editor.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            // 剪贴板被其他程序占用等情况：忽略即可，不打断编辑
            System.Diagnostics.Debug.WriteLine($"粘贴失败：{ex.Message}");
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
