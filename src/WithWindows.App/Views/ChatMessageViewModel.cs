using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using Microsoft.UI.Xaml;
using WithWindows.Core.Chat;

namespace WithWindows.Views;

/// <summary>聊天页中的一条可观察消息。</summary>
public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _content;
    private bool _isMarkdown;

    public ChatMessageViewModel(
        ChatRole role,
        string content,
        bool isMarkdown = false,
        IEnumerable<ChatAttachmentViewModel>? attachments = null)
    {
        Role = role;
        _content = content;
        _isMarkdown = isMarkdown;
        if (attachments is not null)
        {
            foreach (ChatAttachmentViewModel attachment in attachments)
                Attachments.Add(attachment);
        }

        Attachments.CollectionChanged += OnAttachmentsChanged;
    }

    public ChatRole Role { get; }
    public ObservableCollection<ChatAttachmentViewModel> Attachments { get; } = [];

    public string CopyAutomationId =>
        Role == ChatRole.User ? "ChatCopyUserMessage" : "ChatCopyAssistantMessage";

    public Visibility UserVisibility =>
        Role == ChatRole.User ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AssistantVisibility =>
        Role == ChatRole.Assistant ? Visibility.Visible : Visibility.Collapsed;

    public bool HasText => !string.IsNullOrWhiteSpace(Content);
    public bool HasAttachments => Attachments.Count > 0;
    public bool CanCopy => HasText;
    public Visibility TextVisibility => HasText ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AttachmentsVisibility =>
        HasAttachments ? Visibility.Visible : Visibility.Collapsed;

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value)
                return;
            _content = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanCopy)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TextVisibility)));
        }
    }

    public bool IsMarkdown
    {
        get => _isMarkdown;
        set
        {
            if (_isMarkdown == value)
                return;
            _isMarkdown = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsMarkdown)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnAttachmentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAttachments)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AttachmentsVisibility)));
    }
}
