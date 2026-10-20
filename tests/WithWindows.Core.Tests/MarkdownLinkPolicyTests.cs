using WithWindows.Core.Chat;

namespace WithWindows.Core.Tests;

public class MarkdownLinkPolicyTests
{
    [Theory]
    [InlineData("https://example.com", true)]
    [InlineData("http://localhost:8080/a", true)]
    [InlineData("mailto:test@example.com", true)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/secret.txt", false)]
    [InlineData("../relative", false)]
    public void IsAllowed_只允许安全协议(string url, bool expected)
    {
        Assert.Equal(expected, MarkdownLinkPolicy.IsAllowed(url));
    }
}
