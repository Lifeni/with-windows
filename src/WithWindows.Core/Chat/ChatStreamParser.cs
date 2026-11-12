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
            || container.ValueKind != JsonValueKind.Object)
        {
            return new ChatStreamEvent(ChatStreamEventKind.None);
        }

        if (container.TryGetProperty("content", out JsonElement content)
            && content.ValueKind == JsonValueKind.String)
        {
            string? text = content.GetString();
            if (!string.IsNullOrEmpty(text))
                return new ChatStreamEvent(ChatStreamEventKind.Content, text);
        }

        if (TryReadToolCall(container, out ChatToolCallDelta? toolCall))
            return new ChatStreamEvent(
                ChatStreamEventKind.ToolCall,
                ToolCall: toolCall);

        return new ChatStreamEvent(ChatStreamEventKind.None);
    }

    private static bool TryReadToolCall(
        JsonElement container,
        out ChatToolCallDelta? toolCall)
    {
        toolCall = null;
        if (container.TryGetProperty("tool_calls", out JsonElement calls)
            && calls.ValueKind == JsonValueKind.Array
            && calls.GetArrayLength() > 0)
        {
            JsonElement call = calls[0];
            if (call.ValueKind != JsonValueKind.Object)
                return false;

            int index = 0;
            if (call.TryGetProperty("index", out JsonElement indexElement)
                && indexElement.ValueKind == JsonValueKind.Number)
            {
                indexElement.TryGetInt32(out index);
            }

            string? id = ReadOptionalString(call, "id");
            if (!call.TryGetProperty("function", out JsonElement function)
                || function.ValueKind != JsonValueKind.Object)
            {
                toolCall = new ChatToolCallDelta(index, Id: id);
                return true;
            }

            toolCall = new ChatToolCallDelta(
                index,
                id,
                ReadOptionalString(function, "name"),
                ReadArguments(function));
            return true;
        }

        if (container.TryGetProperty("function_call", out JsonElement legacy)
            && legacy.ValueKind == JsonValueKind.Object)
        {
            toolCall = new ChatToolCallDelta(
                0,
                Name: ReadOptionalString(legacy, "name"),
                ArgumentsDelta: ReadArguments(legacy));
            return true;
        }

        return false;
    }

    private static string? ReadArguments(JsonElement function)
    {
        if (!function.TryGetProperty("arguments", out JsonElement arguments))
            return null;

        return arguments.ValueKind switch
        {
            JsonValueKind.String => arguments.GetString(),
            JsonValueKind.Object or JsonValueKind.Array =>
                arguments.GetRawText(),
            _ => null,
        };
    }

    private static string? ReadOptionalString(
        JsonElement parent,
        string property)
    {
        if (!parent.TryGetProperty(property, out JsonElement value)
            || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }
        return value.GetString();
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
