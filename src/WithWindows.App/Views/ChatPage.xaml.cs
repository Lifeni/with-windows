using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Core;
using WithWindows.Controls;
using WithWindows.Core.Chat;
using WithWindows.Core.Diagnostics;
using WithWindows.Services;

namespace WithWindows.Views;

/// <summary>OpenAI 兼容文本流式对话页。</summary>
public sealed partial class ChatPage : Page
{
    private readonly ChatHistoryStore _historyStore =
        new(AppPaths.ChatHistoryPath);
    private readonly ChatSecretStore _secretStore =
        new(AppPaths.ChatCredentialPath);

    private CancellationTokenSource? _requestCts;
    private string _apiKey = "";
    private bool _isSending;
    private bool _isComposing;
    private bool _initialized;

    public static string RouteTag => "ai_chat";

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ChatPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized)
            return;
        _initialized = true;

        try
        {
            ChatSession session = _historyStore.Load();
            foreach (ChatMessage message in session.Messages)
            {
                Messages.Add(new ChatMessageViewModel(
                    message.Role,
                    message.Content,
                    isMarkdown: message.Role == ChatRole.Assistant));
            }
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"AI 会话加载失败：{ex.GetType().Name}");
            ShowError("会话文件损坏，已从空会话开始。");
        }

        try
        {
            _apiKey = await _secretStore.LoadAsync() ?? "";
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"AI 密钥读取失败：{ex.GetType().Name}");
            ShowError("API Key 无法解密，请重新配置。");
        }

        UpdateEmptyState();
        ScrollToLatest();
        ChatInput.Focus(FocusState.Programmatic);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _requestCts?.Cancel();
        if (Messages.Count > 0)
            SaveSession();
    }

    private async void OnSendOrStopClick(object sender, RoutedEventArgs e)
    {
        if (_isSending)
        {
            _requestCts?.Cancel();
            return;
        }

        try
        {
            await SendAsync();
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"AI 发送入口失败：{ex.GetType().Name}");
            ShowError("发送失败，请稍后重试。");
        }
    }

    private async void OnInputKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
            return;

        bool shift =
            (InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                & CoreVirtualKeyStates.Down) == CoreVirtualKeyStates.Down;
        if (shift || _isComposing)
            return;

        e.Handled = true;
        try
        {
            await SendAsync();
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"AI 输入发送失败：{ex.GetType().Name}");
            ShowError("发送失败，请稍后重试。");
        }
    }

    private void OnTextCompositionStarted(
        TextBox sender,
        TextCompositionStartedEventArgs args)
        => _isComposing = true;

    private void OnTextCompositionEnded(
        TextBox sender,
        TextCompositionEndedEventArgs args)
        => _isComposing = false;

    private async Task SendAsync()
    {
        AppHost? host = App.Host;
        if (host is null)
        {
            ShowError("应用尚未完成启动。");
            return;
        }

        string text = ChatInput.Text.Trim();
        string? configError = ChatValidation.Validate(host.Config.Chat);
        if (text.Length == 0 || _isSending || configError is not null)
        {
            ShowError(configError ?? "请输入消息。");
            return;
        }

        var userMessage = new ChatMessageViewModel(ChatRole.User, text);
        Messages.Add(userMessage);
        ChatInput.Text = "";
        ChatErrorBar.IsOpen = false;
        UpdateEmptyState();

        var history = Messages
            .Where(message => message.Role is ChatRole.User or ChatRole.Assistant)
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .Select(message => new ChatMessage(message.Role, message.Content))
            .ToList();

        var assistantMessage = new ChatMessageViewModel(ChatRole.Assistant, "");
        Messages.Add(assistantMessage);
        _requestCts = new CancellationTokenSource();
        SetSending(true);

        try
        {
            var service = new OpenAiCompatibleChatService(host.HttpClient);
            await foreach (ChatDelta delta in service.StreamAsync(
                host.Config.Chat,
                _apiKey,
                history,
                _requestCts.Token))
            {
                assistantMessage.Content += delta.Content;
                ScrollToLatest();
            }

            if (string.IsNullOrEmpty(assistantMessage.Content))
            {
                Messages.Remove(assistantMessage);
                ChatInput.Text = text;
                ShowError("AI 服务没有返回文本。");
                UpdateEmptyState();
                return;
            }

            assistantMessage.IsMarkdown = true;
            SaveSession();
        }
        catch (OperationCanceledException)
        {
            assistantMessage.Content = assistantMessage.Content.Length == 0
                ? "（已停止）"
                : assistantMessage.Content + "\n\n（已停止）";
            assistantMessage.IsMarkdown = true;
            SaveSession();
        }
        catch (ChatRequestException ex)
        {
            HandlePartialAndError(assistantMessage, text, ex.UserMessage);
        }
        catch (Exception ex)
        {
            host.Log.Error($"AI 对话请求失败：{ex.GetType().Name}");
            HandlePartialAndError(
                assistantMessage,
                text,
                "请求失败，请检查端点配置和网络。");
        }
        finally
        {
            _requestCts.Dispose();
            _requestCts = null;
            SetSending(false);
            ChatInput.Focus(FocusState.Programmatic);
        }
    }

    private void HandlePartialAndError(
        ChatMessageViewModel assistant,
        string originalText,
        string error)
    {
        if (string.IsNullOrEmpty(assistant.Content))
        {
            Messages.Remove(assistant);
            ChatInput.Text = originalText;
        }
        else
        {
            assistant.Content += "\n\n（回复不完整）";
            assistant.IsMarkdown = true;
            SaveSession();
        }

        UpdateEmptyState();
        ShowError(error);
    }

    private async void OnConfigureClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ChatProviderDialogResult? result = await ChatProviderDialog.ShowAsync(
                XamlRoot,
                App.Host!.Config.Chat,
                !string.IsNullOrEmpty(_apiKey));
            if (result is null)
                return;

            App.Host.Config.Chat.BaseUrl = result.Value.BaseUrl;
            App.Host.Config.Chat.Model = result.Value.Model;
            App.Host.Config.Chat.SystemPrompt = result.Value.SystemPrompt;

            if (result.Value.ClearApiKey)
            {
                _secretStore.Clear();
                _apiKey = "";
            }
            else if (!string.IsNullOrEmpty(result.Value.NewApiKey))
            {
                await _secretStore.SaveAsync(result.Value.NewApiKey);
                _apiKey = result.Value.NewApiKey;
            }

            App.Host.SaveConfig();
            ChatErrorBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"AI 配置保存失败：{ex.GetType().Name}");
            ShowError("配置保存失败，请检查文件权限。");
        }
    }

    private async void OnClearClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "清空会话",
            Content = "这会删除当前全部对话记录，是否继续？",
            PrimaryButtonText = "清空",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        _requestCts?.Cancel();
        Messages.Clear();
        _historyStore.Clear();
        UpdateEmptyState();
        ChatInput.Focus(FocusState.Programmatic);
    }

    private void OnCopyMessageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string text } || text.Length == 0)
            return;

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private void SaveSession()
    {
        try
        {
            _historyStore.Save(new ChatSession
            {
                UpdatedAt = DateTimeOffset.Now,
                Messages = Messages
                    .Where(message => !string.IsNullOrWhiteSpace(message.Content))
                    .Select(message => new ChatMessage(message.Role, message.Content))
                    .ToList(),
            });
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"AI 会话保存失败：{ex.GetType().Name}");
        }
    }

    private void ShowError(string message)
    {
        ChatErrorBar.Title = "AI 对话";
        ChatErrorBar.Message = message;
        ChatErrorBar.IsOpen = true;
    }

    private void ScrollToLatest()
    {
        if (Messages.Count == 0)
            return;

        ChatMessageViewModel latest = Messages[^1];
        DispatcherQueue.TryEnqueue(() =>
            MessageList.ScrollIntoView(latest, ScrollIntoViewAlignment.Leading));
    }

    private void SetSending(bool sending)
    {
        _isSending = sending;
        ChatSendButton.Content = sending ? "停止" : "发送";
        ChatInput.IsEnabled = !sending;
        AutomationProperties.SetName(
            ChatSendButton,
            sending ? "停止生成" : "发送消息");
    }

    private void UpdateEmptyState()
        => ChatEmptyState.Visibility =
            Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
}
