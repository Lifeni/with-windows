using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class CSharpSyntaxHighlighterTests
{
    [Fact]
    public void Parse_识别关键字类型字符串注释和数字()
    {
        const string code =
            "using var http = new HttpClient(); // 说明\n"
            + "Console.WriteLine(\"你好\");\n"
            + "return true;";

        IReadOnlyList<CSharpSyntaxSpan> spans =
            CSharpSyntaxHighlighter.Parse(code);

        AssertSpan(spans, code, "using", CSharpSyntaxKind.Keyword);
        AssertSpan(spans, code, "var", CSharpSyntaxKind.Keyword);
        AssertSpan(spans, code, "HttpClient", CSharpSyntaxKind.Builtin);
        AssertSpan(spans, code, "// 说明", CSharpSyntaxKind.Comment);
        AssertSpan(spans, code, "\"你好\"", CSharpSyntaxKind.String);
        AssertSpan(spans, code, "return", CSharpSyntaxKind.Keyword);
        AssertSpan(spans, code, "true", CSharpSyntaxKind.Keyword);
    }

    [Fact]
    public void Parse_识别块注释逐字字符串和字符()
    {
        const string code =
            "/* 块\n注释 */\n"
            + "var path = @\"C:\\临时\\文件\";\n"
            + "var quote = '\"';";

        IReadOnlyList<CSharpSyntaxSpan> spans =
            CSharpSyntaxHighlighter.Parse(code);

        AssertSpan(spans, code, "/* 块\n注释 */", CSharpSyntaxKind.Comment);
        AssertSpan(
            spans,
            code,
            "@\"C:\\临时\\文件\"",
            CSharpSyntaxKind.String);
        AssertSpan(spans, code, "'\"'", CSharpSyntaxKind.String);
    }

    [Fact]
    public void Parse_空文本返回空集()
        => Assert.Empty(CSharpSyntaxHighlighter.Parse(""));

    private static void AssertSpan(
        IReadOnlyList<CSharpSyntaxSpan> spans,
        string code,
        string text,
        CSharpSyntaxKind kind)
    {
        Assert.Contains(
            spans,
            span => span.Kind == kind
                && span.Start >= 0
                && span.Start + span.Length <= code.Length
                && code.Substring(span.Start, span.Length) == text);
    }
}
