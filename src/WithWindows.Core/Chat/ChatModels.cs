using WithWindows.Core.Config;

namespace WithWindows.Core.Chat;

/// <summary>对话消息角色。</summary>
public enum ChatRole
{
    System,
    User,
    Assistant,
}

/// <summary>消息附带的一张图片；文件内容保存在会话附件目录。</summary>
public sealed record ChatAttachment(
    string Id,
    string DisplayName,
    string MimeType,
    long ByteLength,
    int Width,
    int Height);

/// <summary>一条完整对话消息。</summary>
public sealed record ChatMessage(ChatRole Role, string Content)
{
    public List<ChatAttachment> Attachments { get; init; } = [];
}

/// <summary>流式回复中的一个文本增量。</summary>
public sealed record ChatDelta(string Content);

/// <summary>OpenAI 兼容对话客户端。</summary>
public interface IChatCompletionService
{
    IAsyncEnumerable<ChatDelta> StreamAsync(
        ChatSettings settings,
        string apiKey,
        IReadOnlyList<ChatMessage> messages,
        CancellationToken cancellationToken);
}

/// <summary>单个 SSE 事件的解析结果。</summary>
public enum ChatStreamEventKind
{
    None,
    Content,
    Done,
    Error,
}

/// <summary>SSE 事件或非流式响应中的文本结果。</summary>
public sealed record ChatStreamEvent(ChatStreamEventKind Kind, string? Text = null);
