using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using WithWindows.Config;

namespace WithWindows.Core;

/// <summary>对话消息（角色 + 内容），用于多轮上下文。</summary>
public sealed record ChatMessage(string Role, string Content);

/// <summary>
/// OpenAI 兼容端口的流式对话客户端（chat/completions + SSE）。零第三方依赖，手写流解析。
/// </summary>
public sealed class AiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>
    /// 发起流式对话（多消息上下文）。onDelta 按片段回调（后台线程，UI 侧需 marshal）；失败时 onError 给原因。
    /// 返回是否成功完成（含收到 [DONE]）。
    /// </summary>
    public async Task<bool> AskAsync(AiConfig config, IReadOnlyList<ChatMessage> messages,
        Action<string> onDelta, Action<string> onError, CancellationToken ct = default)
    {
        try
        {
            string url = config.BaseUrl.TrimEnd('/') + "/chat/completions";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            if (!string.IsNullOrEmpty(config.ApiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", config.ApiKey);

            var payload = new
            {
                model = string.IsNullOrWhiteSpace(config.Model) ? "gpt-3.5-turbo" : config.Model.Trim(),
                messages = messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
                stream = true,
            };
            request.Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(ct);
                onError($"AI 服务返回 {(int)response.StatusCode}：{body.Trim()}");
                return false;
            }

            using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream, Encoding.UTF8);
            while (await reader.ReadLineAsync(ct) is { } line)
            {
                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
                string data = line[5..].Trim();
                if (data == "[DONE]") return true;
                string? delta = ParseSseDelta(data);
                if (delta is not null)
                    onDelta(delta);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            onError("请求已取消");
            return false;
        }
        catch (Exception ex)
        {
            onError($"请求失败：{ex.Message}");
            return false;
        }
    }

    /// <summary>解析单条 SSE data 负载，取出 choices[0].delta.content。纯函数，便于测试。</summary>
    internal static string? ParseSseDelta(string data)
    {
        using var doc = JsonDocument.Parse(data);
        if (!doc.RootElement.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
            return null;
        if (!choices[0].TryGetProperty("delta", out var delta))
            return null;
        if (!delta.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.String)
            return null;
        return content.GetString();
    }
}
