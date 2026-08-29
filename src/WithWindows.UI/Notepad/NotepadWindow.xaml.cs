using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using WithWindows.Config;
using WithWindows.Core;

namespace WithWindows.Notepad;

/// <summary>
/// 快捷记事本：独立置顶窗口，自绘标题栏跟随主题。隐藏/关闭时内容自动复制到剪贴板并保存到
/// notepad.txt（下次打开恢复）。工具栏：粘贴剪贴板、问 AI、AI 设置（弹窗）、清除回复。
/// </summary>
public sealed partial class NotepadWindow : Window
{
    private readonly string _savePath;
    private readonly Logger _log;
    private readonly ConfigStore _configStore;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly AiClient _ai = new();
    private readonly CancellationTokenSource _aiCts = new();
    private bool _sized; // 首次显示时应用初始尺寸；之后保留用户调整

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

    /// <summary>重绘标题栏：内容延伸到标题栏，系统按钮透明，背景随主题。</summary>
    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(TitleBarElement);
        AppWindow.TitleBar.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
        AppWindow.TitleBar.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
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
        UpdateTitle();
    }

    private void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateTitle();
        _saveTimer.Stop();
        _saveTimer.Start(); // 防抖保存
    }

    private void UpdateTitle()
    {
        Title = $"快捷记事（{Editor.Text.Length} 字符）";
        TitleText.Text = Title;
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

        AiPanel.IsExpanded = true;
        AiReply.Text = "";
        AskAiButton.IsEnabled = false;
        AiProgress.IsActive = true;

        bool ok = await _ai.AskAsync(config.Ai, Editor.Text,
            delta => DispatcherQueue.TryEnqueue(() => AiReply.Text += delta),
            error => DispatcherQueue.TryEnqueue(() => AiReply.Text = error),
            _aiCts.Token);

        AiProgress.IsActive = false;
        AskAiButton.IsEnabled = true;
        _log.Info($"[ai] 请求完成: 成功={ok}");
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
