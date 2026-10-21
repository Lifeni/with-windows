using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WithWindows.Core.Config;

namespace WithWindows.Core.Chat;

/// <summary>把内部消息转换为 OpenAI 兼容的 Chat Completions 请求。</summary>
public static class ChatRequestFactory
{
    public static HttpRequestMessage Create(
        ChatSettings settings,
        string apiKey,
        IReadOnlyList<ChatMessage> messages,
        ChatAttachmentStore? attachmentStore = null)
    {
        var effective = new List<ChatMessage>();
        if (!string.IsNullOrWhiteSpace(settings.SystemPrompt))
            effective.Add(new ChatMessage(ChatRole.System, settings.SystemPrompt.Trim()));
        effective.AddRange(messages);

        var payload = new
        {
            model = settings.Model.Trim(),
            messages = effective.Select(message => new
            {
                role = message.Role switch
                {
                    ChatRole.System => "system",
                    ChatRole.User => "user",
                    _ => "assistant",
                },
                content = CreateContent(message, attachmentStore),
            }).ToArray(),
            stream = true,
        };

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            ChatValidation.CreateCompletionsUri(settings.BaseUrl))
        {
            Content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json"),
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        return request;
    }

    private static object CreateContent(
        ChatMessage message,
        ChatAttachmentStore? attachmentStore)
    {
        if (message.Attachments.Count == 0)
            return message.Content;
        if (attachmentStore is null)
            throw new InvalidOperationException("发送图片消息需要附件存储。");

        var parts = new List<object>();
        if (!string.IsNullOrWhiteSpace(message.Content))
            parts.Add(new { type = "text", text = message.Content });

        foreach (ChatAttachment attachment in message.Attachments)
        {
            parts.Add(new
            {
                type = "image_url",
                image_url = new { url = attachmentStore.LoadDataUrl(attachment) },
            });
        }

        return parts;
    }
}
