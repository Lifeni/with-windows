using System.Text;

namespace WithWindows.Core.Scheduling;

public enum LuaSyntaxKind
{
    Keyword,
    Builtin,
    String,
    Comment,
    Number,
}

public readonly record struct LuaSyntaxSpan(
    int Start,
    int Length,
    LuaSyntaxKind Kind);

/// <summary>轻量 Lua 5.4 词法高亮，不尝试构建完整语法树。</summary>
public static class LuaSyntaxHighlighter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for",
        "function", "goto", "if", "in", "local", "nil", "not", "or",
        "repeat", "return", "then", "true", "until", "while",
    };

    private static readonly HashSet<string> Builtins = new(StringComparer.Ordinal)
    {
        "assert", "collectgarbage", "coroutine", "debug", "dofile", "error",
        "getmetatable", "io", "ipairs", "load", "loadfile", "math", "next",
        "os", "package", "pairs", "pcall", "print", "rawequal", "rawget",
        "rawlen", "rawset", "require", "select", "setmetatable", "string",
        "table", "tonumber", "tostring", "type", "utf8", "warn", "xpcall",
    };

    public static IReadOnlyList<LuaSyntaxSpan> Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var spans = new List<LuaSyntaxSpan>();
        int index = 0;
        while (index < text.Length)
        {
            char current = text[index];
            if (char.IsWhiteSpace(current))
            {
                index++;
                continue;
            }

            if (current == '-' && index + 1 < text.Length && text[index + 1] == '-')
            {
                int start = index;
                int contentStart = index + 2;
                int end = TryReadLongBracket(text, contentStart, out int bracketEnd)
                    ? bracketEnd
                    : ReadToLineEnd(text, contentStart);
                spans.Add(new LuaSyntaxSpan(start, end - start, LuaSyntaxKind.Comment));
                index = end;
                continue;
            }

            if (current is '\'' or '"')
            {
                int end = ReadQuotedString(text, index, current);
                spans.Add(new LuaSyntaxSpan(index, end - index, LuaSyntaxKind.String));
                index = end;
                continue;
            }

            if (current == '[' && TryReadLongBracket(text, index, out int longStringEnd))
            {
                spans.Add(new LuaSyntaxSpan(
                    index,
                    longStringEnd - index,
                    LuaSyntaxKind.String));
                index = longStringEnd;
                continue;
            }

            if (char.IsAsciiDigit(current)
                || current == '.' && index + 1 < text.Length && char.IsAsciiDigit(text[index + 1]))
            {
                int end = ReadNumber(text, index);
                spans.Add(new LuaSyntaxSpan(index, end - index, LuaSyntaxKind.Number));
                index = end;
                continue;
            }

            if (IsIdentifierStart(current))
            {
                int end = index + 1;
                while (end < text.Length && IsIdentifierPart(text[end]))
                    end++;

                string word = text[index..end];
                LuaSyntaxKind? kind = Keywords.Contains(word)
                    ? LuaSyntaxKind.Keyword
                    : Builtins.Contains(word)
                        ? LuaSyntaxKind.Builtin
                        : null;
                if (kind is { } value)
                    spans.Add(new LuaSyntaxSpan(index, end - index, value));

                index = end;
                continue;
            }

            index++;
        }

        return spans;
    }

    private static int ReadQuotedString(string text, int start, char quote)
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
            if (current == quote)
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
            if (char.IsAsciiLetterOrDigit(current) || current is '.' or '_')
            {
                index++;
                continue;
            }

            if (current is '+' or '-'
                && index > start
                && text[index - 1] is 'e' or 'E' or 'p' or 'P')
            {
                index++;
                continue;
            }

            break;
        }

        return index;
    }

    private static bool TryReadLongBracket(
        string text,
        int start,
        out int endExclusive)
    {
        endExclusive = start;
        if (start >= text.Length || text[start] != '[')
            return false;

        int equalsEnd = start + 1;
        while (equalsEnd < text.Length && text[equalsEnd] == '=')
            equalsEnd++;
        if (equalsEnd >= text.Length || text[equalsEnd] != '[')
            return false;

        int equalsCount = equalsEnd - start - 1;
        string closing = "]" + new string('=', equalsCount) + "]";
        int contentStart = equalsEnd + 1;
        int closingIndex = text.IndexOf(closing, contentStart, StringComparison.Ordinal);
        endExclusive = closingIndex < 0
            ? text.Length
            : closingIndex + closing.Length;
        return true;
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
