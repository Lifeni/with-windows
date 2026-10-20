using System.Net;
using System.Text;
using System.Text.Json;

namespace WithWindows.Core.Chat;

/// <summary>可安全展示给用户的对话请求错误。</summary>
public sealed class ChatRequestException : Exception
{
    public HttpStatusCode? StatusCode { get; }
    public string UserMessage { get; }

    public ChatRequestException(HttpStatusCode? statusCode, string userMessage)
        : base(userMessage)
    {
        StatusCode = statusCode;
        UserMessage = userMessage;
    }

    /// <summary>读取有限长度的服务端摘要并转换为中文提示。</summary>
    public static async Task<ChatRequestException> CreateAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        string body = "";
        try
        {
            await using Stream stream =
                await response.Content.ReadAsStreamAsync(cancellationToken);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            char[] buffer = new char[4096];
            int read = await reader.ReadBlockAsync(buffer.AsMemory(), cancellationToken);
            body = new string(buffer, 0, read);
        }
        catch (Exception)
        {
        }

        string? serviceMessage = null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out JsonElement error)
                && error.ValueKind == JsonValueKind.Object
                && error.TryGetProperty("message", out JsonElement message)
                && message.ValueKind == JsonValueKind.String)
            {
                serviceMessage = Sanitize(message.GetString());
            }
        }
        catch (JsonException)
        {
        }

        int status = (int)response.StatusCode;
        string userMessage = status switch
        {
            401 or 403 => "认证失败，请检查 API Key。",
            404 => "接口不存在，请检查 Base URL 是否包含正确路径。",
            429 => "请求过于频繁，请稍后重试。",
            >= 500 => "AI 服务暂时不可用，请稍后重试。",
            _ => "请求失败，请检查端点配置。",
        };

        if (!string.IsNullOrWhiteSpace(serviceMessage))
            userMessage += " " + serviceMessage;

        return new ChatRequestException(response.StatusCode, userMessage);
    }

    private static string? Sanitize(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return null;

        string oneLine = message
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Trim();
        return oneLine.Length <= 200 ? oneLine : oneLine[..200] + "…";
    }
}
