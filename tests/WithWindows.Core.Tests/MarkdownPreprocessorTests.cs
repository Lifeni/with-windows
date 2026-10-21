using WithWindows.Core.Chat;

namespace WithWindows.Core.Tests;

public class MarkdownPreprocessorTests
{
    [Fact]
    public void NormalizeDetails_HTML折叠块转为容器()
    {
        string markdown =
            """
            <details>
            <summary>更多信息</summary>

            - 第一项
            - 第二项

            </details>
            """;

        string result = MarkdownPreprocessor.NormalizeDetails(markdown);

        Assert.Contains("::: details 更多信息", result);
        Assert.Contains("- 第一项", result);
        Assert.EndsWith(":::", result.TrimEnd());
    }

    [Fact]
    public void NormalizeDetails_代码块内不转换()
    {
        const string markdown =
            """
            ```html
            <details><summary>示例</summary></details>
            ```
            """;

        string expected = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        Assert.Equal(expected, MarkdownPreprocessor.NormalizeDetails(markdown));
    }
}
