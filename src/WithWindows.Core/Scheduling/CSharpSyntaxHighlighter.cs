using System.Text;

namespace WithWindows.Core.Scheduling;

public enum CSharpSyntaxKind
{
    Keyword,
    Builtin,
    String,
    Comment,
    Number,
}

public readonly record struct CSharpSyntaxSpan(
    int Start,
    int Length,
    CSharpSyntaxKind Kind);

/// <summary>轻量 C# 词法高亮，不尝试构建完整语法树。</summary>
public static class CSharpSyntaxHighlighter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "async", "await", "base", "break", "case", "catch",
        "checked", "class", "const", "continue", "default", "delegate", "do",
        "else", "enum", "event", "explicit", "extern", "false", "finally",
        "fixed", "for", "foreach", "goto", "if", "implicit", "in", "interface",
        "internal", "is", "lock", "namespace", "new", "null", "operator",
        "out", "override", "params", "private", "protected", "public",
        "readonly", "record", "ref", "return", "sealed", "sizeof", "stackalloc",
        "static", "struct", "switch", "this", "throw", "true", "try", "typeof",
        "unchecked", "unsafe", "using", "var", "virtual", "volatile", "while",
        "yield",
    };

    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "bool", "byte", "char", "decimal", "double", "dynamic", "float", "int",
        "long", "nint", "nuint", "object", "sbyte", "short", "string", "uint",
        "ulong", "ushort", "Console", "DateTime", "DateTimeOffset", "Encoding",
        "Enumerable", "Environment", "File", "HttpClient", "JsonSerializer",
        "Math", "Path", "Process", "Regex", "Task", "TimeSpan", "Uri",
    };

    public static IReadOnlyList<CSharpSyntaxSpan> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spans = new List<CSharpSyntaxSpan>();
        int index = 0;
        while (index < text.Length)
        {
            char current = text[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '/' && index + 1 < text.Length)
            {
                if (text[index + 1] == '/')
                {
                    int end = ReadToLineEnd(text, index + 2);
                    spans.Add(new CSharpSyntaxSpan(
                        index,
                        end - index,
                        CSharpSyntaxKind.Comment));
                    index = end;
                    continue;
                }

                if (text[index + 1] == '*')
                {
                    int closing = text.IndexOf("*/", index + 2, StringComparison.Ordinal);
                    int end = closing < 0 ? text.Length : closing + 2;
                    spans.Add(new CSharpSyntaxSpan(
                        index,
                        end - index,
                        CSharpSyntaxKind.Comment));
                    index = end;
                    continue;
                }
            }

            if (current == '$'
                && index + 1 < text.Length
                && text[index + 1] is '"' or '@')
            {
                int end = ReadString(text, index);
                spans.Add(new CSharpSyntaxSpan(
                    index,
                    end - index,
                    CSharpSyntaxKind.String));
                index = end;
                continue;
            }

            if (current == '@'
                && index + 1 < text.Length
                && text[index + 1] == '"')
            {
                int end = ReadVerbatimString(text, index + 1);
                spans.Add(new CSharpSyntaxSpan(
                    index,
                    end - index,
                    CSharpSyntaxKind.String));
                index = end;
                continue;
            }

            if (current is '"' or '\'')
            {
                int end = current == '"'
                    ? ReadString(text, index)
                    : ReadChar(text, index);
                spans.Add(new CSharpSyntaxSpan(
                    index,
                    end - index,
                    CSharpSyntaxKind.String));
                index = end;
                continue;
            }

            if (char.IsAsciiDigit(current)
                || current == '.'
                && index + 1 < text.Length
                && char.IsAsciiDigit(text[index + 1]))
            {
                int end = ReadNumber(text, index);
                spans.Add(new CSharpSyntaxSpan(
                    index,
                    end - index,
                    CSharpSyntaxKind.Number));
                index = end;
                continue;
            }

            if (IsIdentifierStart(current))
            {
                int end = index + 1;
                while (end < text.Length && IsIdentifierPart(text[end]))
                    end++;

                string word = text[index..end];
                CSharpSyntaxKind? kind = Keywords.Contains(word)
                    ? CSharpSyntaxKind.Keyword
                    : Builtins.Contains(word)
                        ? CSharpSyntaxKind.Builtin
                        : null;
                if (kind is { } value)
                {
                    spans.Add(new CSharpSyntaxSpan(
                        index,
                        end - index,
                        value));
                }

                index = end;
                continue;
            }

            index++;
        }

        return spans;
    }

    private static int ReadString(string text, int start)
    {
        int quoteIndex = start;
        if (text[quoteIndex] == '$')
            quoteIndex++;
        if (quoteIndex < text.Length && text[quoteIndex] == '@')
            quoteIndex++;
        if (quoteIndex >= text.Length || text[quoteIndex] != '"')
            return start + 1;

        int quoteCount = 1;
        while (quoteIndex + quoteCount < text.Length
            && text[quoteIndex + quoteCount] == '"')
        {
            quoteCount++;
        }

        if (quoteCount >= 3)
        {
            string delimiter = new('"', quoteCount);
            int closing = text.IndexOf(
                delimiter,
                quoteIndex + quoteCount,
                StringComparison.Ordinal);
            return closing < 0
                ? text.Length
                : closing + quoteCount;
        }

        if (quoteIndex > start
            && quoteIndex - 1 >= start
            && text[quoteIndex - 1] == '@')
        {
            return ReadVerbatimString(text, quoteIndex);
        }

        int index = quoteIndex + 1;
        while (index < text.Length)
        {
            char current = text[index];
            if (current == '\\' && index + 1 < text.Length)
            {
                index += 2;
                continue;
            }
            if (current == '"')
                return index + 1;
            if (current is '\r' or '\n')
                return index;
            index++;
        }

        return text.Length;
    }

    private static int ReadVerbatimString(string text, int quoteIndex)
    {
        int index = quoteIndex + 1;
        while (index < text.Length)
        {
            if (text[index] == '"')
            {
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            index++;
        }

        return text.Length;
    }

    private static int ReadChar(string text, int start)
    {
        int index = start + 1;
        while (index < text.Length)
        {
            char current = text[index];
            if (current == '\\' && index + 1 < text.Length)
            {
                index += 2;
                continue;
            }
            if (current == '\'')
                return index + 1;
            if (current is '\r' or '\n')
                return index;
            index++;
        }

        return text.Length;
    }

    private static int ReadNumber(string text, int start)
    {
        int index = start;
        while (index < text.Length)
        {
            char current = text[index];
            if (char.IsAsciiLetterOrDigit(current)
                || current is '.' or '_')
            {
                index++;
                continue;
            }

            if (current is '+' or '-'
                && index > start
                && text[index - 1] is 'e' or 'E')
            {
                index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static int ReadToLineEnd(string text, int start)
    {
        int index = start;
        while (index < text.Length && text[index] is not ('\r' or '\n'))
            index++;
        return index;
    }

    private static bool IsIdentifierStart(char value)
        => value == '_' || char.IsAsciiLetter(value);

    private static bool IsIdentifierPart(char value)
        => value == '_' || char.IsAsciiLetterOrDigit(value);
}
