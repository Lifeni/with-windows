using System.Text.Json;

namespace WithWindows.Core.Chat;

/// <summary>解析 Chat Completions 的 SSE 事件和普通 JSON 响应。</summary>
public static class ChatStreamParser
{
    public static ChatStreamEvent Parse(string data)
    {
        string payload = data.Trim();
        if (payload == "[DONE]")
            return new ChatStreamEvent(ChatStreamEventKind.Done);
        if (payload.Length == 0)
            return new ChatStreamEvent(ChatStreamEventKind.None);

        try
        {
            using JsonDocument document = JsonDocument.Parse(payload);
            JsonElement root = document.RootElement;
            if (TryReadError(root, out string? error))
                return new ChatStreamEvent(ChatStreamEventKind.Error, error);
            return ReadChoice(root, "delta");
        }
        catch (JsonException)
        {
            return new ChatStreamEvent(
                ChatStreamEventKind.Error,
                "服务返回了无法解析的流式数据。");
        }
    }

    public static ChatStreamEvent ParseCompletion(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (TryReadError(root, out string? error))
                return new ChatStreamEvent(ChatStreamEventKind.Error, error);
            return ReadChoice(root, "message");
        }
        catch (JsonException)
        {
            return new ChatStreamEvent(
                ChatStreamEventKind.Error,
                "服务返回了无法解析的响应数据。");
        }
    }

    private static ChatStreamEvent ReadChoice(JsonElement root, string containerName)
    {
        if (!root.TryGetProperty("choices", out JsonElement choices)
            || choices.ValueKind != JsonValueKind.Array
            || choices.GetArrayLength() == 0)
        {
            return new ChatStreamEvent(ChatStreamEventKind.None);
        }

        JsonElement choice = choices[0];
        if (!choice.TryGetProperty(containerName, out JsonElement container)
            || container.ValueKind != JsonValueKind.Object
            || !container.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.String)
        {
            return new ChatStreamEvent(ChatStreamEventKind.None);
        }

        string? text = content.GetString();
        return string.IsNullOrEmpty(text)
            ? new ChatStreamEvent(ChatStreamEventKind.None)
            : new ChatStreamEvent(ChatStreamEventKind.Content, text);
    }

    private static bool TryReadError(JsonElement root, out string? message)
    {
        message = null;
        if (!root.TryGetProperty("error", out JsonElement error))
            return false;

        if (error.ValueKind == JsonValueKind.String)
        {
            message = error.GetString();
        }
        else if (error.ValueKind == JsonValueKind.Object
                 && error.TryGetProperty("message", out JsonElement detail)
                 && detail.ValueKind == JsonValueKind.String)
        {
            message = detail.GetString();
        }

        message = string.IsNullOrWhiteSpace(message) ? "服务返回错误。" : message;
        return true;
    }
}
