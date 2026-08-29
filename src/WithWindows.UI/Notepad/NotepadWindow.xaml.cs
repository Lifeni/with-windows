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
/// 快捷记事本：独立置顶窗口。打开时若剪贴板有文本且与正文不同，底部显示建议条，点击追加；
/// 隐藏/关闭时内容自动复制到剪贴板并保存到 notepad.txt（下次打开恢复）。
/// 内置 AI 助手：把编辑区内容发给 OpenAI 兼容端口，流式回复显示在可折叠面板。
/// </summary>
public sealed partial class NotepadWindow : Window
{
    private readonly string _savePath;
    private readonly Logger _log;
    private readonly ConfigStore _configStore;
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly AiClient _ai = new();
    private readonly CancellationTokenSource _aiCts = new();
    private string? _suggestionText;
    private bool _sized; // 首次显示时应用初始尺寸；之后保留用户调整

    /// <summary>窗口当前是否可见（热键切换判断）。</summary>
    public bool Visible => AppWindow.IsVisible;

    public NotepadWindow(string savePath, Logger log, ConfigStore configStore)
    {
        _savePath = savePath;
        _log = log;
        _configStore = configStore;
        InitializeComponent();

        if (AppWindow.Presenter is OverlappedPresenter presenter)
            presenter.IsAlwaysOnTop = true; // 始终置顶：随时弹出记录

        _saveTimer = DispatcherQueue.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(400);
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); Save(); };

        LoadSavedText();
        LoadAiConfig();
        Closed += OnClosed;
    }

    /// <summary>显示并聚焦；每次显示时刷新剪贴板建议条。</summary>
    public async void ShowAndFocus()
    {
        Activate();
        if (!_sized)
        {
            AppWindow.Resize(new SizeInt32(820, 620));
            _sized = true;
        }
        await ShowClipboardSuggestion();
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
    }

    // ---- 剪贴板建议条 ----

    private async Task ShowClipboardSuggestion()
    {
        _suggestionText = null;
        ClipSuggestion.Visibility = Visibility.Collapsed;
        try
        {
            var content = Clipboard.GetContent();
            if (content.Contains(StandardDataFormats.Text))
            {
                string? text = await content.GetTextAsync();
                if (!string.IsNullOrEmpty(text) && text != Editor.Text)
                {
                    _suggestionText = text;
                    ClipSuggestionText.Text = text.Length > 60 ? text[..60] + "…" : text;
                    ClipSuggestion.Visibility = Visibility.Visible;
                }
            }
        }
        catch
        {
            // 剪贴板被占用等异常：静默隐藏建议条，不打扰记录
            ClipSuggestion.Visibility = Visibility.Collapsed;
        }
    }

    private void OnClipSuggestionClick(object sender, RoutedEventArgs e)
    {
        if (_suggestionText is null) return;
        Editor.Text += _suggestionText;
        Editor.SelectionStart = Editor.Text.Length; // 光标移到末尾
        _suggestionText = null;
        ClipSuggestion.Visibility = Visibility.Collapsed;
    }

    // ---- AI 助手 ----

    private void LoadAiConfig()
    {
        try
        {
            var config = _configStore.Load();
            AiBaseUrl.Text = config.Ai.BaseUrl;
            AiApiKey.Text = config.Ai.ApiKey;
            AiModel.Text = config.Ai.Model;
        }
        catch (Exception ex)
        {
            _log.Error($"AI 配置读取失败: {ex}");
        }
    }

    private void OnSaveAiConfig(object sender, RoutedEventArgs e)
    {
        try
        {
            var config = _configStore.Load();
            config.Ai.BaseUrl = AiBaseUrl.Text.Trim();
            config.Ai.ApiKey = AiApiKey.Text.Trim();
            config.Ai.Model = AiModel.Text.Trim();
            _configStore.Save(config);
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
            AiReply.Text = "请先在“AI 配置”中填写 Base URL";
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
        _saveTimer.Stop();
        Save();
        CopyToClipboard();
    }
}
