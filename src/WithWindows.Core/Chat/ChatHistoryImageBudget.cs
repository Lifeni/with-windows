namespace WithWindows.Core.Chat;

/// <summary>限制发往模型的历史图片数量和总字节数，最新图片优先保留。</summary>
public static class ChatHistoryImageBudget
{
    public const int MaxImages = 4;
    public const long MaxBytes = 4 * 1024 * 1024;
    public const string OmittedImagePlaceholder = "（较早的图片已省略）";

    public static IReadOnlyList<ChatMessage> Limit(
        IReadOnlyList<ChatMessage> messages,
        int maxImages = MaxImages,
        long maxBytes = MaxBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxImages);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);

        var keep = new bool[messages.Count][];
        int remainingImages = maxImages;
        long remainingBytes = maxBytes;

        for (int messageIndex = messages.Count - 1; messageIndex >= 0; messageIndex--)
        {
            IReadOnlyList<ChatAttachment> attachments = messages[messageIndex].Attachments;
            keep[messageIndex] = new bool[attachments.Count];

            for (int attachmentIndex = attachments.Count - 1; attachmentIndex >= 0; attachmentIndex--)
            {
                if (remainingImages == 0)
                    break;

                ChatAttachment attachment = attachments[attachmentIndex];
                if (attachment.ByteLength < 0 || attachment.ByteLength > remainingBytes)
                    continue;

                keep[messageIndex][attachmentIndex] = true;
                remainingImages--;
                remainingBytes -= attachment.ByteLength;
            }
        }

        var limited = new List<ChatMessage>(messages.Count);
        for (int messageIndex = 0; messageIndex < messages.Count; messageIndex++)
        {
            ChatMessage message = messages[messageIndex];
            List<ChatAttachment> attachments = message.Attachments
                .Where((_, index) => keep[messageIndex][index])
                .ToList();

            if (string.IsNullOrWhiteSpace(message.Content)
                && attachments.Count == 0
                && message.Role != ChatRole.System)
            {
                if (message.Role != ChatRole.User || message.Attachments.Count == 0)
                    continue;

                limited.Add(message with
                {
                    Content = OmittedImagePlaceholder,
                    Attachments = attachments,
                });
                continue;
            }

            limited.Add(message with { Attachments = attachments });
        }

        return limited;
    }
}
