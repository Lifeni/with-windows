using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
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
    private bool _initializingSession;
    private int _scrollGeneration;

    public static string RouteTag => "ai_chat";

    public ObservableCollection<ChatMessageViewModel> Messages { get; } = [];

    public ObservableCollection<ChatAttachmentViewModel> PendingAttachments { get; } = [];

    private bool CanTouchUi =>
        IsLoaded
        && XamlRoot is not null
        && App.Host is not { IsShuttingDown: true };

    public ChatPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;

    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        LogInfo($"AI 页 Loaded：initialized={_initialized}");
        if (_initializingSession)
            return;

        if (!_initialized)
        {
            _initializingSession = true;
            SetInitializing(true);
            try
            {
                var loadedMessages = new List<ChatMessageViewModel>();
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
                                if (!CanTouchUi)
                                    return;
                            }
                            catch (Exception ex) when (ex is IOException or ArgumentException)
                            {
                                LogError($"AI 图片附件读取失败：{ex.GetType().Name}");
                            }
                        }

                        if (!CanTouchUi)
                            return;

                        loadedMessages.Add(new ChatMessageViewModel(
                            message.Role,
                            message.Content,
                            isMarkdown: message.Role == ChatRole.Assistant,
                            attachments));
                    }
                }
                catch (Exception ex)
                {
                    loadedMessages.Clear();
                    LogError($"AI 会话加载失败：{ex.GetType().Name}");
                    ShowError("会话文件损坏，已从空会话开始。");
                }

                string loadedApiKey = "";
                try
                {
                    loadedApiKey = await _secretStore.LoadAsync() ?? "";
                }
                catch (Exception ex)
                {
                    LogError($"AI 密钥读取失败：{ex.GetType().Name}");
                    ShowError("API Key 无法解密，请重新配置。");
                }

                if (!CanTouchUi)
                    return;

                Messages.Clear();
                foreach (ChatMessageViewModel message in loadedMessages)
                    Messages.Add(message);
                _apiKey = loadedApiKey;
                _initialized = true;
            }
            finally
            {
                _initializingSession = false;
                if (CanTouchUi)
                    SetInitializing(false);
            }
        }

        if (!CanTouchUi)
            return;

        ApplyUiState();
        ScrollToLatest();

        if (App.MainWindow is { } window
            && Core.Interop.WindowInterop.IsForeground(
                WinRT.Interop.WindowNative.GetWindowHandle(window)))
        {
            FocusInput();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        LogInfo($"AI 页 Unloaded：messages={Messages.Count}，sending={_isSending}");
        bool wasSending = _isSending;
        _requestCts?.Cancel();
        if (_initialized && !_initializingSession && Messages.Count > 0)
            SaveSession(markStoppedResponse: wasSending);
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
            LogError($"AI 输入发送失败：{ex.GetType().Name}");
            ShowError("发送失败，请稍后重试。");
        }
    }

    private void OnPageKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            if (_isSending)
            {
                e.Handled = true;
                _requestCts?.Cancel();
            }
        }
    }

    private void FocusInput()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!CanTouchUi || _isReadingClipboard)
                return;
            if (App.MainWindow is not { } window
                || !Core.Interop.WindowInterop.IsForeground(
                    WinRT.Interop.WindowNative.GetWindowHandle(window)))
            {
                return;
            }

            ChatInput.Focus(FocusState.Programmatic);
        });
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
        if (_initializingSession)
            return;

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
            LogError($"读取剪贴板图片失败：{ex.GetType().Name}");
            ShowError("剪贴板图片读取失败。");
        }
    }

    private async Task AttachClipboardImagesAsync()
    {
        if (_isSending || _isReadingClipboard || _initializingSession)
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
            if (!CanTouchUi)
                return;

            if (images.Count == 0)
            {
                ShowError("剪贴板中没有可用的图片。当前仅支持图片，其他文件不会附加。");
                return;
            }

            var attachments = new List<ChatAttachmentViewModel>();
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
                if (!CanTouchUi)
                    return;
                attachments.Add(attachment);
            }

            foreach (ChatAttachmentViewModel attachment in attachments)
                PendingAttachments.Add(attachment);
            if (images.Count > slots)
                ShowError("每条消息最多附加 4 张图片，超出的图片未添加。");
            UpdatePendingAttachmentsBar();
            ChatErrorBar.IsOpen = false;
            FocusInput();
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
        if (_initializingSession)
        {
            ShowError("正在加载当前会话，请稍候。");
            return;
        }

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
            LogError($"AI 图片保存失败：{ex.GetType().Name}");
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
        UpdateHeaderInfo();

        List<ChatMessage> history = BuildHistory();
        var assistantMessage = new ChatMessageViewModel(ChatRole.Assistant, "");
        Messages.Add(assistantMessage);
        UpdateHeaderInfo();
        ScrollToLatest();
        _requestCts = new CancellationTokenSource();
        SetSending(true);
        var streamedContent = new StringBuilder();

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
                streamedContent.Append(delta.Content);
                if (!CanTouchUi)
                {
                    _requestCts.Cancel();
                    SaveSession(
                        markStoppedResponse: true,
                        stoppedAssistantContent: streamedContent.ToString());
                    return;
                }

                assistantMessage.Content = streamedContent.ToString();
                ScrollToLatest();
            }

            if (!CanTouchUi)
            {
                SaveSession(
                    markStoppedResponse: true,
                    stoppedAssistantContent: streamedContent.ToString());
                return;
            }

            if (streamedContent.Length == 0)
            {
                Messages.Remove(assistantMessage);
                Messages.Remove(userMessage);
                DeleteAttachmentModels(originalAttachments);
                RestoreDraft(text, originalAttachments);
                ShowError("AI 服务没有返回文本。");
                UpdateEmptyState();
                UpdateHeaderInfo();
                return;
            }

            assistantMessage.Content = streamedContent.ToString();
            assistantMessage.IsMarkdown = true;
            SaveSession();
            UpdateHeaderInfo();
            ScrollToLatest();
        }
        catch (OperationCanceledException)
        {
            if (!CanTouchUi)
            {
                SaveSession(
                    markStoppedResponse: true,
                    stoppedAssistantContent: streamedContent.ToString());
                return;
            }

            string partialContent = streamedContent.ToString();
            assistantMessage.Content = partialContent.Length == 0
                ? "（已停止）"
                : partialContent + "\n\n（已停止）";
            assistantMessage.IsMarkdown = true;
            SaveSession();
            UpdateHeaderInfo();
            ScrollToLatest();
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
            FocusInput();
        }
    }

    private void HandleRequestFailure(
        ChatMessageViewModel assistant,
        ChatMessageViewModel userMessage,
        string originalText,
        List<ChatAttachmentViewModel> originalAttachments,
        string error)
    {
        if (!CanTouchUi)
            return;

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
        UpdateHeaderInfo();
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
        if (_initializingSession)
            return;

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
            if (!CanTouchUi)
                return;

            ChatErrorBar.IsOpen = false;
            UpdateEmptyState();
            UpdateHeaderInfo();
        }
        catch (Exception ex)
        {
            LogError($"AI 配置保存失败：{ex.GetType().Name}");
            ShowError("配置保存失败，请检查文件权限。");
        }
    }

    private async void OnNewSessionClick(object sender, RoutedEventArgs e)
    {
        if (_initializingSession)
            return;

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
        if (!CanTouchUi)
        {
            _historyStore.Clear();
            try
            {
                _attachmentStore.Clear();
            }
            catch (Exception ex)
            {
                LogError($"AI 图片附件清理失败：{ex.GetType().Name}");
            }

            _initialized = false;
            return;
        }

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
            LogError($"AI 图片附件清理失败：{ex.GetType().Name}");
            ShowError("部分图片附件未能清理。");
        }

        UpdateEmptyState();
        UpdateHeaderInfo();
        FocusInput();
    }

    private void SaveSession(
        bool markStoppedResponse = false,
        string? stoppedAssistantContent = null)
    {
        try
        {
            List<ChatMessage> messages = BuildHistory();
            if (markStoppedResponse && _isSending)
                MarkLastAssistantStopped(messages, stoppedAssistantContent);

            _historyStore.Save(new ChatSession
            {
                UpdatedAt = DateTimeOffset.Now,
                Messages = messages,
            });
        }
        catch (Exception ex)
        {
            LogError($"AI 会话保存失败：{ex.GetType().Name}");
        }
    }

    private static void MarkLastAssistantStopped(
        List<ChatMessage> messages,
        string? contentOverride)
    {
        const string marker = "（已停止）";
        if (messages.Count > 0
            && messages[^1] is { Role: ChatRole.Assistant } assistant)
        {
            string content = contentOverride ?? assistant.Content;
            if (string.IsNullOrWhiteSpace(content))
                content = marker;
            else if (!content.EndsWith(marker, StringComparison.Ordinal))
                content += "\n\n" + marker;

            messages[^1] = assistant with { Content = content };
            return;
        }

        string stoppedContent = string.IsNullOrWhiteSpace(contentOverride)
            ? marker
            : contentOverride.EndsWith(marker, StringComparison.Ordinal)
                ? contentOverride
                : contentOverride + "\n\n" + marker;
        messages.Add(new ChatMessage(ChatRole.Assistant, stoppedContent));
    }

    private void ShowError(string message)
    {
        if (!CanTouchUi)
            return;

        ChatErrorBar.Title = "AI 对话";
        ChatErrorBar.Message = message;
        ChatErrorBar.IsOpen = true;
    }

    private void UpdateHeaderInfo()
    {
        if (!CanTouchUi)
            return;

        ChatSessionTitle.Text = ChatSessionSummary.Create(BuildHistory());

        string? model = App.Host?.Config.Chat.Model;
        ChatModelInfo.Text = string.IsNullOrWhiteSpace(model)
            ? "未配置"
            : model.Trim();
    }

    private void ScrollToLatest()
    {
        if (!CanTouchUi)
            return;

        int generation = ++_scrollGeneration;
        QueueScrollToLatest(generation, remainingAttempts: 2);
    }

    private void QueueScrollToLatest(int generation, int remainingAttempts)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            if (!CanTouchUi || generation != _scrollGeneration)
                return;

            MessageList.UpdateLayout();
            ScrollViewer? scrollViewer = FindScrollViewer(MessageList);
            if (scrollViewer is null)
            {
                if (remainingAttempts > 0)
                    QueueScrollToLatest(generation, remainingAttempts - 1);
                return;
            }

            double targetOffset = scrollViewer.ScrollableHeight;
            bool changed = scrollViewer.ChangeView(
                null,
                targetOffset,
                null,
                disableAnimation: true);
            if (!changed && Messages.Count > 0)
                MessageList.ScrollIntoView(Messages[^1], ScrollIntoViewAlignment.Default);

            bool reachedBottom =
                scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 1d;
            if ((!changed || !reachedBottom) && remainingAttempts > 0)
                QueueScrollToLatest(generation, remainingAttempts - 1);
        });
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer scrollViewer)
            return scrollViewer;

        int childCount = VisualTreeHelper.GetChildrenCount(root);
        for (int index = 0; index < childCount; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(root, index);
            if (FindScrollViewer(child) is { } nested)
                return nested;
        }

        return null;
    }

    private void SetSending(bool sending)
    {
        _isSending = sending;
        if (!CanTouchUi)
            return;

        ChatInput.IsEnabled = !sending && !_initializingSession;
        ChatInput.PlaceholderText = sending ? "正在回复…按 Esc 停止" : "与 AI 对话";
        UpdateBusyRing();
    }

    private void ApplyUiState()
    {
        if (!CanTouchUi)
            return;

        ChatNewSessionButton.IsEnabled = true;
        ChatConfigureButton.IsEnabled = true;
        ChatInput.IsEnabled = !_isSending && !_initializingSession;
        ChatInput.PlaceholderText = _isSending ? "正在回复…按 Esc 停止" : "与 AI 对话";
        UpdateBusyRing();
        UpdateEmptyState();
        UpdatePendingAttachmentsBar();
        UpdateHeaderInfo();
    }

    private void SetAttachmentReading(bool reading)
    {
        _isReadingClipboard = reading;
        UpdateBusyRing();
    }

    private void SetInitializing(bool initializing)
    {
        if (!CanTouchUi)
            return;

        ChatInput.IsEnabled = !initializing && !_isSending;
        ChatInput.PlaceholderText = initializing
            ? "正在加载会话…"
            : _isSending
                ? "正在回复…按 Esc 停止"
                : "与 AI 对话";
        ChatNewSessionButton.IsEnabled = !initializing;
        ChatConfigureButton.IsEnabled = !initializing;
        UpdateBusyRing();
    }

    private void UpdateBusyRing()
    {
        if (!CanTouchUi)
            return;

        bool busy = _isSending || _isReadingClipboard || _initializingSession;
        ChatBusyRing.IsActive = busy;
        ChatBusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateEmptyState()
    {
        if (!CanTouchUi)
            return;

        bool empty = Messages.Count == 0;
        ChatEmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (!empty)
            return;

        bool configured = App.Host is not null
            && ChatValidation.Validate(App.Host.Config.Chat) is null;
        ChatEmptyState.Text = configured
            ? "输入消息或粘贴图片开始新的对话。"
            : "请先点击右上角配置 OpenAI 兼容接口。";
    }

    private void UpdatePendingAttachmentsBar()
    {
        if (!CanTouchUi)
            return;

        PendingAttachmentsBar.Visibility =
            PendingAttachments.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static void LogInfo(string message)
    {
        if (App.Host is { IsShuttingDown: false } host)
            host.Log.Info(message);
    }

    private static void LogError(string message)
    {
        if (App.Host is { IsShuttingDown: false } host)
            host.Log.Error(message);
    }
}
