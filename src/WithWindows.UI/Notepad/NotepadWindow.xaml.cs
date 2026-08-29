using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows.Notepad;

/// <summary>
/// 快捷记事本：独立置顶窗口，标题栏兼工具栏（粘贴剪贴板 / 问 AI / AI 设置）。
/// 底部状态栏显示行列与字符数；AI 回复区常驻显示。隐藏/关闭时内容自动复制到剪贴板并保存。
/// </summary>
public sealed partial class NotepadWindow : Window
{
    private readonly string _savePath;
    private readonly Logger _log;
    private readonly ConfigStore _configStore;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly AiClient _ai = new();
    private readonly CancellationTokenSource _aiCts = new();
    private bool _sized;

    /// <summary>窗口当前是否可见（热键切换判断）。</summary>
    public bool Visible => AppWindow.IsVisible;

    public NotepadWindow(string savePath, Logger log, ConfigStore configStore)
    {
        _savePath = savePath;
        _log = log;
        _configStore = configStore;
        InitializeComponent();

        SetupTitleBar();
        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true; // 始终置顶：随时弹出记录

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
            AppWindow.Resize(new SizeInt32(820, 620));
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

    /// <summary>状态栏：光标行列 + 总字符数。</summary>
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

    // ---- 工具栏：粘贴剪贴板 ----

    private async void OnClipboardPaste(object sender, RoutedEventArgs e)
    {
        try
        {
            var content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;

            string? text = await content.GetTextAsync();
            if (string.IsNullOrEmpty(text)) return;

            Editor.Text += text;
            Editor.SelectionStart = Editor.Text.Length; // 光标移到末尾
        }
        catch (Exception ex)
        {
            _log.Error($"剪贴板读取失败: {ex}");
        }
    }

    // ---- AI 助手 ----

    private async void OnOpenAiConfig(object sender, RoutedEventArgs e)
    {
        var config = _configStore.Load();
        var urlBox = new TextBox { Header = "Base URL", Text = config.Ai.BaseUrl, PlaceholderText = "http://127.0.0.1:11434/v1" };
        var keyBox = new TextBox { Header = "API Key", Text = config.Ai.ApiKey, PlaceholderText = "留空则不携带" };
        var modelBox = new TextBox { Header = "模型", Text = config.Ai.Model, PlaceholderText = "如 qwen2.5" };

        var panel = new StackPanel { Spacing = 12, MinWidth = 320 };
        panel.Children.Add(urlBox);
        panel.Children.Add(keyBox);
        panel.Children.Add(modelBox);

        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = "AI 设置",
            Content = panel,
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var updated = _configStore.Load();
            updated.Ai.BaseUrl = urlBox.Text.Trim();
            updated.Ai.ApiKey = keyBox.Text.Trim();
            updated.Ai.Model = modelBox.Text.Trim();
            _configStore.Save(updated);
            _log.Info("[ai] 配置已保存");
        }
        catch (Exception ex)
        {
            _log.Error($"AI 配置保存失败: {ex}");
        }
    }

    private async void OnAskAi(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(Editor.Text))
        {
            AiReply.Text = "请先在编辑区输入内容";
            return;
        }

        var config = _configStore.Load();
        if (string.IsNullOrWhiteSpace(config.Ai.BaseUrl))
        {
            AiReply.Text = "请先在 AI 设置中填写 Base URL";
            return;
        }

        AiReply.Text = "";
        AiPanelButton.IsChecked = true; // 展开侧栏（触发 OnAiPanelToggle）
        AskAiButton.IsEnabled = false;
        AiProgress.IsActive = true;

        bool ok = await _ai.AskAsync(config.Ai, Editor.Text,
            delta => DispatcherQueue.TryEnqueue(() => AiReply.Text += delta),
            error => DispatcherQueue.TryEnqueue(() => AiReply.Text = error),
            _aiCts.Token);

        AiProgress.IsActive = false;
        AskAiButton.IsEnabled = true;
        _log.Info($"[ai] 请求完成: 成功={ok}");

        // 回复完成后自动收起侧栏（内容保留，可再展开查看）
        var closeTimer = DispatcherQueue.CreateTimer();
        closeTimer.Interval = TimeSpan.FromSeconds(1);
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            AiPanelButton.IsChecked = false; // 收起侧栏（触发 OnAiPanelToggle）
        };
        closeTimer.Start();
    }

    /// <summary>侧栏按钮手动切换。</summary>
    private void OnAiPanelToggle(object sender, RoutedEventArgs e)
        => SetAiPanelVisible(AiPanelButton.IsChecked == true);

    /// <summary>显示/隐藏 AI 回复侧栏（列宽随动）。IsChecked 由触发方维护，避免事件递归。</summary>
    private void SetAiPanelVisible(bool visible)
    {
        AiColumn.Width = visible ? new GridLength(300) : new GridLength(0);
        AiSidePanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClearAi(object sender, RoutedEventArgs e)
    {
        AiReply.Text = "";
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
