using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
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
    private void ApplyLineSpacing()
    {
        var format = Editor.Document.GetDefaultParagraphFormat();
        format.SetLineSpacing(LineSpacingRule.Multiple, 1.05f);
        Editor.Document.SetDefaultParagraphFormat(format);

        var selection = Editor.Document.Selection;
        selection.SetRange(0, int.MaxValue);
        selection.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, 1.05f);
        selection.SetRange(0, 0);
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

    /// <summary>保存：把防抖中的草稿立即写盘（关闭程序不会自动复制到剪贴板，复制是显式操作）。</summary>
    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        Editor.Document.GetText(TextGetOptions.None, out string text);
        _editorBaseline = text;
        _draft?.QueueSave(text);
        _draft?.Flush();
        StatusCount.Text = $"{text.TrimEnd('\r').Length} 个字符（已保存）";
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

        // 未开启时不显示对勾（控件本身在解析期就会被调用，此时 WrapCheck 可能尚未构造）
        if (WrapCheck is not null)
            WrapCheck.Visibility = WrapToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
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
