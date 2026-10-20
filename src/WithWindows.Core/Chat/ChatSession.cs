namespace WithWindows.Core.Chat;

/// <summary>当前 AI 对话记录。</summary>
public sealed class ChatSession
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;
    public List<ChatMessage> Messages { get; set; } = [];
}
