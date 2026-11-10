using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class LuaSyntaxHighlighterTests
{
    [Fact]
    public void Parse_识别关键字内置函数字符串注释和数字()
    {
        const string code =
            "local value = 42 -- 说明\n"
            + "print('你好')\n"
            + "function f() return true end";

        IReadOnlyList<LuaSyntaxSpan> spans = LuaSyntaxHighlighter.Parse(code);

        AssertSpan(spans, code, "local", LuaSyntaxKind.Keyword);
        AssertSpan(spans, code, "42", LuaSyntaxKind.Number);
        AssertSpan(spans, code, "-- 说明", LuaSyntaxKind.Comment);
        AssertSpan(spans, code, "print", LuaSyntaxKind.Builtin);
        AssertSpan(spans, code, "'你好'", LuaSyntaxKind.String);
        AssertSpan(spans, code, "function", LuaSyntaxKind.Keyword);
        AssertSpan(spans, code, "return", LuaSyntaxKind.Keyword);
        AssertSpan(spans, code, "true", LuaSyntaxKind.Keyword);
    }

    [Fact]
    public void Parse_识别块注释长字符串和转义引号()
    {
        const string code =
            "--[[ 块\n注释 ]]\n"
            + "local text = [[长字符串]]\n"
            + "local quoted = \"a\\\"b\"";

        IReadOnlyList<LuaSyntaxSpan> spans = LuaSyntaxHighlighter.Parse(code);

        AssertSpan(spans, code, "--[[ 块\n注释 ]]", LuaSyntaxKind.Comment);
        AssertSpan(spans, code, "[[长字符串]]", LuaSyntaxKind.String);
        AssertSpan(spans, code, "\"a\\\"b\"", LuaSyntaxKind.String);
    }

    [Fact]
    public void Parse_空文本返回空集()
        => Assert.Empty(LuaSyntaxHighlighter.Parse(""));

    private static void AssertSpan(
        IReadOnlyList<LuaSyntaxSpan> spans,
        string code,
        string text,
        LuaSyntaxKind kind)
    {
        Assert.Contains(
            spans,
            span => span.Kind == kind
                && span.Start >= 0
                && span.Start + span.Length <= code.Length
                && code.Substring(span.Start, span.Length) == text);
    }
}
