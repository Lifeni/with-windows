using WithWindows.Core.Config;

namespace WithWindows.Core.Chat;

/// <summary>对话端点和消息的轻量校验。</summary>
public static class ChatValidation
{
    /// <summary>规范化兼容端点根地址；只接受不带查询和片段的 HTTP(S) 地址。</summary>
    public static string NormalizeBaseUrl(string input)
    {
        string trimmed = input.Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out Uri? uri)
            || uri.Scheme is not ("http" or "https")
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw new ArgumentException("请输入有效的 HTTP 或 HTTPS 地址。", nameof(input));
        }

        return trimmed.TrimEnd('/');
    }

    /// <summary>构造 Chat Completions 地址。</summary>
    public static Uri CreateCompletionsUri(string baseUrl)
        => new(NormalizeBaseUrl(baseUrl) + "/chat/completions", UriKind.Absolute);

    /// <summary>返回配置问题；null 表示可以发送。</summary>
    public static string? Validate(ChatSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.BaseUrl))
            return "请先配置 Base URL。";
        if (string.IsNullOrWhiteSpace(settings.Model))
            return "请先配置模型。";

        try
        {
            _ = NormalizeBaseUrl(settings.BaseUrl);
            return null;
        }
        catch (ArgumentException ex)
        {
            return ex.Message;
        }
    }
}
