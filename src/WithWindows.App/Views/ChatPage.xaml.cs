using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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

/// <summary>OpenAI 兼容流式对话页，支持剪贴板图片。</summary>
public sealed partial class ChatPage : Page
{
    private const int MaxPendingImages = 4;

    private readonly ChatHistoryStore _historyStore =
        new(AppPaths.ChatHistoryPath);
    private readonly ChatSecretStore _secretStore =
        new(AppPaths.ChatCredentialPath);
    private readonly ChatAttachmentStore _attachmentStore =
        new(AppPaths.ChatAttachmentsDirectory);

    private CancellationTokenSource? _requestCts;
    private string _apiKey = "";
    private bool _isSending;
    private bool _isComposing;
    private bool _isReadingClipboard;
    private bool _initialized;

    public static string RouteTag => "ai_chat";

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<ChatAttachmentViewModel> PendingAttachments { get; } = [];

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
                var attachments = new List<ChatAttachmentViewModel>();
                foreach (ChatAttachment attachment in message.Attachments)
                {
                    try
                    {
                        byte[] bytes = _attachmentStore.LoadBytes(attachment);
                        attachments.Add(await ChatAttachmentViewModel.CreateAsync(
                            bytes,
                            attachment.DisplayName,
                            attachment.MimeType,
                            attachment.Width,
                            attachment.Height,
                            wasCompressed: false,
                            attachment));
                    }
                    catch (Exception ex) when (ex is IOException or ArgumentException)
                    {
                        App.Host?.Log.Error($"AI 图片附件读取失败：{ex.GetType().Name}");
                    }
                }

                Messages.Add(new ChatMessageViewModel(
                    message.Role,
                    message.Content,
                    isMarkdown: message.Role == ChatRole.Assistant,
                    attachments));
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
        UpdatePendingAttachmentsBar();
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

    private async void OnInputPaste(object sender, TextControlPasteEventArgs args)
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (!ClipboardImageService.ContainsPotentialImage(content))
                return;

            args.Handled = true;
            await AttachClipboardImagesAsync();
        }
        catch (Exception ex)
        {
            args.Handled = true;
            App.Host?.Log.Error($"读取剪贴板图片失败：{ex.GetType().Name}");
            ShowError("剪贴板图片读取失败。");
        }
    }

    private async void OnAttachImageClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await AttachClipboardImagesAsync();
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"读取剪贴板图片失败：{ex.GetType().Name}");
            ShowError("剪贴板图片读取失败。");
        }
    }

    private async Task AttachClipboardImagesAsync()
    {
        if (_isSending || _isReadingClipboard)
            return;
        int slots = MaxPendingImages - PendingAttachments.Count;
        if (slots <= 0)
        {
            ShowError("每条消息最多附加 4 张图片。");
            return;
        }

        SetAttachmentReading(true);
        try
        {
            IReadOnlyList<ClipboardImageData> images =
                await ClipboardImageService.ReadAsync();
            if (images.Count == 0)
            {
                ShowError("剪贴板中没有可用的图片。当前仅支持图片，其他文件不会附加。");
                return;
            }

            foreach (ClipboardImageData image in images.Take(slots))
            {
                ChatAttachmentViewModel attachment =
                    await ChatAttachmentViewModel.CreateAsync(
                        image.Bytes,
                        image.DisplayName,
                        image.MimeType,
                        image.Width,
                        image.Height,
                        image.WasCompressed);
                PendingAttachments.Add(attachment);
            }

            if (images.Count > slots)
                ShowError("每条消息最多附加 4 张图片，超出的图片未添加。");
            UpdatePendingAttachmentsBar();
            ChatErrorBar.IsOpen = false;
            ChatInput.Focus(FocusState.Programmatic);
        }
        finally
        {
            SetAttachmentReading(false);
        }
    }

    private void OnRemovePendingAttachmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement
            {
                DataContext: ChatAttachmentViewModel attachment,
            })
        {
            PendingAttachments.Remove(attachment);
            UpdatePendingAttachmentsBar();
        }
    }

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
        bool hasImages = PendingAttachments.Count > 0;
        if (_isSending
            || configError is not null
            || (text.Length == 0 && !hasImages))
        {
            ShowError(configError ?? "请输入消息或粘贴图片。");
            return;
        }

        List<ChatAttachmentViewModel> originalAttachments =
            PendingAttachments.ToList();
        try
        {
            foreach (ChatAttachmentViewModel attachment in originalAttachments)
            {
                attachment.Model = _attachmentStore.Save(
                    attachment.Bytes,
                    attachment.DisplayName,
                    attachment.MimeType,
                    attachment.Width,
                    attachment.Height);
            }
        }
        catch (Exception ex)
        {
            DeleteAttachmentModels(originalAttachments);
            App.Host?.Log.Error($"AI 图片保存失败：{ex.GetType().Name}");
            ShowError("图片保存失败，请重新粘贴。");
            return;
        }

        var userMessage = new ChatMessageViewModel(
            ChatRole.User,
            text,
            attachments: originalAttachments);
        Messages.Add(userMessage);
        ChatInput.Text = "";
        PendingAttachments.Clear();
        UpdatePendingAttachmentsBar();
        ChatErrorBar.IsOpen = false;
        UpdateEmptyState();

        List<ChatMessage> history = BuildHistory();
        var assistantMessage = new ChatMessageViewModel(ChatRole.Assistant, "");
        Messages.Add(assistantMessage);
        _requestCts = new CancellationTokenSource();
        SetSending(true);

        try
        {
            var service = new OpenAiCompatibleChatService(
                host.HttpClient,
                _attachmentStore);
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
                Messages.Remove(userMessage);
                DeleteAttachmentModels(originalAttachments);
                RestoreDraft(text, originalAttachments);
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
            HandleRequestFailure(
                assistantMessage,
                userMessage,
                text,
                originalAttachments,
                ex.UserMessage);
        }
        catch (Exception ex)
        {
            host.Log.Error($"AI 对话请求失败：{ex.GetType().Name}");
            HandleRequestFailure(
                assistantMessage,
                userMessage,
                text,
                originalAttachments,
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

    private void HandleRequestFailure(
        ChatMessageViewModel assistant,
        ChatMessageViewModel userMessage,
        string originalText,
        List<ChatAttachmentViewModel> originalAttachments,
        string error)
    {
        if (string.IsNullOrEmpty(assistant.Content))
        {
            Messages.Remove(assistant);
            Messages.Remove(userMessage);
            DeleteAttachmentModels(originalAttachments);
            RestoreDraft(originalText, originalAttachments);
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

    private void RestoreDraft(
        string text,
        List<ChatAttachmentViewModel> attachments)
    {
        ChatInput.Text = text;
        foreach (ChatAttachmentViewModel attachment in attachments)
        {
            attachment.Model = null;
            PendingAttachments.Add(attachment);
        }

        UpdatePendingAttachmentsBar();
    }

    private void DeleteAttachmentModels(
        IEnumerable<ChatAttachmentViewModel> attachments)
    {
        foreach (ChatAttachmentViewModel attachment in attachments)
        {
            if (attachment.Model is not null)
            {
                try
                {
                    _attachmentStore.Delete(attachment.Model);
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private List<ChatMessage> BuildHistory()
        => Messages
            .Where(message => message.HasText || message.HasAttachments)
            .Select(message => new ChatMessage(message.Role, message.Content)
            {
                Attachments = message.Attachments
                    .Where(attachment => attachment.Model is not null)
                    .Select(attachment => attachment.Model!)
                    .ToList(),
            })
            .ToList();

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

    private async void OnNewSessionClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "新建会话",
            Content = "这会清空当前对话和图片附件，是否继续？",
            PrimaryButtonText = "新建",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            return;

        _requestCts?.Cancel();
        Messages.Clear();
        PendingAttachments.Clear();
        UpdatePendingAttachmentsBar();
        _historyStore.Clear();
        try
        {
            _attachmentStore.Clear();
        }
        catch (Exception ex)
        {
            App.Host?.Log.Error($"AI 图片附件清理失败：{ex.GetType().Name}");
            ShowError("部分图片附件未能清理。");
        }

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
                Messages = BuildHistory(),
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
        ChatAttachImageButton.IsEnabled = !sending && !_isReadingClipboard;
        AutomationProperties.SetName(
            ChatSendButton,
            sending ? "停止生成" : "发送消息");
    }

    private void SetAttachmentReading(bool reading)
    {
        _isReadingClipboard = reading;
        ChatAttachmentProgress.IsActive = reading;
        ChatAttachmentProgress.Visibility =
            reading ? Visibility.Visible : Visibility.Collapsed;
        ChatAttachImageButton.IsEnabled = !reading && !_isSending;
    }

    private void UpdateEmptyState()
        => ChatEmptyState.Visibility =
            Messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void UpdatePendingAttachmentsBar()
        => PendingAttachmentsBar.Visibility =
            PendingAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
}
