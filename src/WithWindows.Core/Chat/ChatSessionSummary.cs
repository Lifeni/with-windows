using System.Globalization;
using System.Text;

namespace WithWindows.Core.Chat;

/// <summary>根据首条用户消息生成短会话标题。</summary>
public static class ChatSessionSummary
{
    public static string Create(IEnumerable<ChatMessage> messages)
    {
        ChatMessage? firstUser = messages.FirstOrDefault(
            message => message.Role == ChatRole.User
                && (!string.IsNullOrWhiteSpace(message.Content)
                    || message.Attachments.Count > 0));
        if (firstUser is null)
            return "AI 对话";
        if (string.IsNullOrWhiteSpace(firstUser.Content))
            return "图片对话";

        string summary = string.Join(
            ' ',
            firstUser.Content.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return TruncateTextElements(summary, 36);
    }

    private static string TruncateTextElements(string text, int maxElements)
    {
        TextElementEnumerator enumerator = StringInfo.GetTextElementEnumerator(text);
        var builder = new StringBuilder();
        int elements = 0;
        while (enumerator.MoveNext())
        {
            if (elements == maxElements)
                return builder.Append('…').ToString();

            builder.Append(enumerator.GetTextElement());
            elements++;
        }

        return text;
    }
}
