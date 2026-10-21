using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text.Json;
using WithWindows.Core.Config;

namespace WithWindows.Core.Chat;

/// <summary>通过 Chat Completions 和 SSE 访问 OpenAI 兼容端点。</summary>
public sealed class OpenAiCompatibleChatService(
    HttpClient http,
    ChatAttachmentStore? attachmentStore = null) : IChatCompletionService
{
    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        ChatSettings settings,
        string apiKey,
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using HttpRequestMessage request =
            ChatRequestFactory.Create(settings, apiKey, messages, attachmentStore);
        using HttpResponseMessage response = await http.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw await ChatRequestException.CreateAsync(response, cancellationToken);

        await using Stream stream =
            await response.Content.ReadAsStreamAsync(cancellationToken);

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        bool isJson = mediaType is not null
            && (mediaType.EndsWith("/json", StringComparison.OrdinalIgnoreCase)
                || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase));

        if (!isJson)
        {
            SseParser<string> parser = SseParser.Create(stream);
            await foreach (SseItem<string> sse in parser.EnumerateAsync(cancellationToken))
            {
                ChatStreamEvent item = ChatStreamParser.Parse(sse.Data);
                if (item.Kind == ChatStreamEventKind.Done)
                    yield break;
                if (item.Kind == ChatStreamEventKind.Error)
                    throw new ChatRequestException(
                        null,
                        item.Text ?? "服务返回错误。");
                if (item.Kind == ChatStreamEventKind.Content
                    && !string.IsNullOrEmpty(item.Text))
                {
                    yield return new ChatDelta(item.Text);
                }
            }

            yield break;
        }

        using JsonDocument document = await JsonDocument.ParseAsync(
            stream,
            cancellationToken: cancellationToken);
        ChatStreamEvent completion =
            ChatStreamParser.ParseCompletion(document.RootElement.GetRawText());
        if (completion.Kind == ChatStreamEventKind.Error)
            throw new ChatRequestException(null, completion.Text ?? "服务返回错误。");
        if (completion.Kind == ChatStreamEventKind.Content
            && !string.IsNullOrEmpty(completion.Text))
        {
            yield return new ChatDelta(completion.Text);
        }
    }
}
