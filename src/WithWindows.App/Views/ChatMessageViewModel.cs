using System.ComponentModel;
using Microsoft.UI.Xaml;
using WithWindows.Core.Chat;

namespace WithWindows.Views;

/// <summary>聊天页中的一条可观察消息。</summary>
public sealed class ChatMessageViewModel : INotifyPropertyChanged
{
    private string _content;
    private bool _isMarkdown;

    public ChatMessageViewModel(ChatRole role, string content, bool isMarkdown = false)
    {
        Role = role;
        _content = content;
        _isMarkdown = isMarkdown;
    }

    public ChatRole Role { get; }

    public string CopyAutomationId =>
        Role == ChatRole.User ? "ChatCopyUserMessage" : "ChatCopyAssistantMessage";

    public Visibility UserVisibility =>
        Role == ChatRole.User ? Visibility.Visible : Visibility.Collapsed;

    public Visibility AssistantVisibility =>
        Role == ChatRole.Assistant ? Visibility.Visible : Visibility.Collapsed;

    public string Content
    {
        get => _content;
        set
        {
            if (_content == value)
                return;
            _content = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Content)));
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
}
