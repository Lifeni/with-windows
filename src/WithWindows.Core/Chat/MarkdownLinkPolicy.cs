namespace WithWindows.Core.Chat;

/// <summary>Markdown 链接的协议允许列表。</summary>
public static class MarkdownLinkPolicy
{
    public static bool IsAllowed(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return false;
        return uri.Scheme is "http" or "https" or "mailto";
    }
}
