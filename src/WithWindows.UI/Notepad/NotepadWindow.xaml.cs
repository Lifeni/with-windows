using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.System;
using Windows.UI.Core;
using CommunityToolkit.WinUI.UI.Controls;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Notepad;

/// <summary>
/// 快捷记事本：竖长置顶窗口。普通模式为纯文本编辑（Ctrl+C/V 复制粘贴，Ctrl+S 另存为，关闭自动复制到剪贴板并保存）；
/// "AI 模式"把编辑区切换为聊天视图（上方对话、下方输入发送），进入时自动把当前文本发给 AI，未配置则提示前往设置。
/// </summary>
public sealed partial class NotepadWindow : Window
{
    private readonly string _savePath;
    private readonly Logger _log;
    private readonly ConfigStore _configStore;
    private readonly Action _onOpenSettings;
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer _saveTimer;
    private readonly AiClient _ai = new();
    private readonly CancellationTokenSource _aiCts = new();
    private readonly List<ChatMessage> _chatHistory = new();
    private bool _sized;

    /// <summary>窗口当前是否可见（热键切换判断）。</summary>
    public new bool Visible => AppWindow.IsVisible;

    public NotepadWindow(string savePath, Logger log, ConfigStore configStore, Action onOpenSettings)
    {
        _savePath = savePath;
        _log = log;
        _configStore = configStore;
        _onOpenSettings = onOpenSettings;
        InitializeComponent();

        SetupTitleBar();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true; // 始终置顶：随时弹出记录
        // 竖长形态与尺寸限制（Win32 WM_GETMINMAXINFO）
        WindowSizeLimits.Apply(WinRT.Interop.WindowNative.GetWindowHandle(this), 420, 600, 720, 1200);

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };

        LoadSavedText();
        Closed += OnClosed;
    }

    /// <summary>重绘标题栏并设置窗口图标：内容延伸到标题栏，系统按钮透明，背景随主题。</summary>
    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarElement);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;

        string icoPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "with-windows.ico");
        IntPtr hIcon = NativeMethods.LoadImage(IntPtr.Zero, icoPath, 1 /* IMAGE_ICON */, 0, 0, 0x10 /* LR_LOADFROMFILE */);
        if (hIcon != IntPtr.Zero)
            AppWindow.SetIcon(new IconId((ulong)hIcon));
    }

    /// <summary>显示并聚焦。</summary>
    public void ShowAndFocus()
    {
        Activate();
        if (!_sized)
        {
            AppWindow.Resize(new SizeInt32(520, 780));
            _sized = true;
        }
    }

    /// <summary>复制内容到剪贴板并隐藏（热键再次按下）。</summary>
    public void CopyAndHide()
    {
        CopyToClipboard();
        AppWindow.Hide();
    }

    private void LoadSavedText()
    {
        try
        {
            if (File.Exists(_savePath))
                Editor.Text = File.ReadAllText(_savePath);
        }
        catch (Exception ex)
        {
            _log.Error($"记事本读取失败: {ex}");
        }
        UpdateStatus();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateStatus();
        _saveTimer.Stop();
        _saveTimer.Start(); // 防抖保存
    }

    private void OnSelectionChanged(object sender, RoutedEventArgs e) => UpdateStatus();

    /// <summary>状态栏：光标行列 + 总字符数，并同步窗口标题。</summary>
    private void UpdateStatus()
    {
        string text = Editor.Text;
        int start = Math.Clamp(Editor.SelectionStart, 0, text.Length);
        int line = 1, column = 1;
        for (int i = 0; i < start; i++)
        {
            if (text[i] == '\n') { line++; column = 1; }
            else column++;
        }
        StatusText.Text = $"行 {line}，列 {column}　·　共 {text.Length} 字符";
        string title = $"快捷记事（{text.Length} 字符）";
        Title = title;
        AppWindow.Title = title; // 自绘标题栏下 Window.Title 不同步 Win32 文本，需显式设置
    }

    // ---- 快捷键：Ctrl+S 另存为（Ctrl+C/V 由 TextBox 原生支持） ----

    private void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.S && IsCtrlDown())
        {
            e.Handled = true;
            _ = SaveAsAsync();
        }
    }

    private void OnChatInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter && !IsShiftDown())
        {
            e.Handled = true;
            _ = SendChatAsync();
        }
    }

    private static bool IsCtrlDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control).HasFlag(CoreVirtualKeyStates.Down);

    private static bool IsShiftDown()
        => InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift).HasFlag(CoreVirtualKeyStates.Down);

    // ---- 工具栏 ----

    private void OnOpenSettings(object sender, RoutedEventArgs e) => _onOpenSettings();

    /// <summary>另存为（Ctrl+S / 工具栏）。</summary>
    private async Task SaveAsAsync()
    {
        try
        {
            var picker = new Windows.Storage.Pickers.FileSavePicker();
            WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));
            picker.FileTypeChoices.Add("文本文件", new List<string> { ".txt" });
            picker.SuggestedFileName = $"记事本-{DateTime.Now:yyyyMMdd-HHmmss}";

            var file = await picker.PickSaveFileAsync();
            if (file is null) return;
            await Windows.Storage.FileIO.WriteTextAsync(file, Editor.Text);
            _log.Info($"[notepad] 已另存为: {file.Path}");
        }
        catch (Exception ex)
        {
            _log.Error($"另存为失败: {ex}");
        }
    }

    // ---- AI 聊天模式 ----

    /// <summary>AI 模式开关：开启后编辑区变为聊天视图，自动把当前文本发给 AI。</summary>
    private void OnAiModeToggle(object sender, RoutedEventArgs e)
    {
        bool on = AiModeButton.IsChecked == true;
        if (on)
        {
            EditorGridVisible(false);
            ChatMessages.Children.Clear();
            _chatHistory.Clear();

            string text = Editor.Text.Trim();
            if (text.Length > 0)
                _ = AskAsync(text); // 自动提问
            ChatInput.Focus(FocusState.Programmatic);
        }
        else
        {
            EditorGridVisible(true);
        }
    }

    private void EditorGridVisible(bool editorVisible)
    {
        Editor.Visibility = editorVisible ? Visibility.Visible : Visibility.Collapsed;
        AiChatGrid.Visibility = editorVisible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnSendChat(object sender, RoutedEventArgs e) => await SendChatAsync();

    private async Task SendChatAsync()
    {
        string text = ChatInput.Text.Trim();
        if (text.Length == 0) return;
        await AskAsync(text);
    }

    /// <summary>发送消息并流式接收回复；未配置 AI 时提示并给出前往设置的入口。</summary>
    private async Task AskAsync(string userText)
    {
        var config = _configStore.Load();
        if (string.IsNullOrWhiteSpace(config.Ai.BaseUrl))
        {
            AddChatBubble("assistant", "尚未配置 AI，点击下方按钮前往设置。");
            var goBtn = new Button { Content = "前往设置", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4) };
            goBtn.Click += (_, _) => _onOpenSettings();
            ChatMessages.Children.Add(goBtn);
            _log.Info("[ai] 未配置，提示前往设置");
            return;
        }

        _chatHistory.Add(new ChatMessage("user", userText));
        AddChatBubble("user", userText);
        ChatInput.Text = "";
        SendButton.IsEnabled = false;
        AiProgress.IsActive = true;

        var reply = new MarkdownTextBlock { Text = "" };
        AddChatBubble("assistant", reply, getCopyText: () => reply.Text);

        bool ok = await _ai.AskAsync(config.Ai, _chatHistory,
            delta => DispatcherQueue.TryEnqueue(() => reply.Text += delta),
            error => DispatcherQueue.TryEnqueue(() => reply.Text = error),
            _aiCts.Token);

        AiProgress.IsActive = false;
        SendButton.IsEnabled = true;
        _chatHistory.Add(new ChatMessage("assistant", reply.Text));
        _log.Info($"[ai] 请求完成: 成功={ok}");

        // 滚动到底部
        DispatcherQueue.TryEnqueue(() => ChatScroller.ChangeView(null, ChatScroller.ScrollableHeight, null));
    }

    /// <summary>向对话区追加一条消息气泡（用户右对齐、AI 左对齐；AI 消息支持 Markdown 渲染并带复制按钮）。</summary>
    private void AddChatBubble(string role, string content)
    {
        FrameworkElement contentBlock = role == "assistant"
            ? new MarkdownTextBlock { Text = content }
            : new TextBlock { Text = content, TextWrapping = TextWrapping.Wrap };
        AddChatBubble(role, contentBlock, role == "assistant" ? () => content : null);
    }

    private void AddChatBubble(string role, FrameworkElement content, Func<string>? getCopyText = null)
    {
        var bubble = new Grid { ColumnSpacing = 8, MaxWidth = 420 };
        bubble.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bubble.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var contentHost = new Border
        {
            Background = role == "user"
                ? (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["AccentFillColorDefaultBrush"]
                : (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12, 8, 12, 8),
        };
        contentHost.Child = content;
        bubble.Children.Add(contentHost);

        if (getCopyText is not null)
        {
            var copyBtn = new Button
            {
                Padding = new Thickness(6),
                VerticalAlignment = VerticalAlignment.Top,
                Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
            };
            Grid.SetColumn(copyBtn, 1);
            ToolTipService.SetToolTip(copyBtn, "复制回复");
            copyBtn.Content = new FontIcon { Glyph = "\uE8C8", FontSize = 12 };
            copyBtn.Click += (_, _) =>
            {
                var data = new DataPackage();
                data.SetText(getCopyText());
                Clipboard.SetContent(data);
            };
            bubble.Children.Add(copyBtn);
        }

        bubble.HorizontalAlignment = role == "user" ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        ChatMessages.Children.Add(bubble);
    }

    // ---- 保存与剪贴板 ----

    private void Save()
    {
        try
        {
            File.WriteAllText(_savePath, Editor.Text);
        }
        catch (Exception ex)
        {
            _log.Error($"记事本保存失败: {ex}");
        }
    }

    private void CopyToClipboard()
    {
        try
        {
            var data = new DataPackage();
            data.SetText(Editor.Text);
            Clipboard.SetContent(data);
        }
        catch (Exception ex)
        {
            _log.Error($"剪贴板写入失败: {ex}");
        }
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _aiCts.Cancel(); // 中断进行中的 AI 请求
        _saveTimer.Stop();
        Save();
        CopyToClipboard();
    }
}
