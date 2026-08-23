using System.Text;

namespace WithWindows.Config;

/// <summary>极简 JSON 值类型：仅对象/数组/字符串（本项目配置所需子集）。</summary>
internal abstract class JsonValue
{
    public static JsonValue Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));
        var parser = new Parser(text);
        var value = parser.ParseValue();
        parser.SkipWhitespace();
        if (!parser.AtEnd)
            throw new FormatException("JSON 末尾存在多余内容");
        return value;
    }
}

internal sealed class JsonObject : JsonValue
{
    private readonly Dictionary<string, JsonValue> _fields = new(StringComparer.Ordinal);

    public IReadOnlyCollection<KeyValuePair<string, JsonValue>> Fields => _fields;

    public void Add(string key, JsonValue value) => _fields.Add(key, value);

    public bool TryGet(string key, out JsonValue? value) => _fields.TryGetValue(key, out value);
}

internal sealed class JsonArray : JsonValue
{
    public List<JsonValue> Items { get; } = new();
}

internal sealed class JsonString : JsonValue
{
    public string Value { get; }

    public JsonString(string value) => Value = value;
}

/// <summary>递归下降 JSON 解析器（对象/数组/字符串子集）。</summary>
internal static class MiniJson
{
    public static JsonValue Parse(string text) => JsonValue.Parse(text);
}

internal sealed class Parser
{
    private readonly string _text;
    private int _pos;

    public Parser(string text) => _text = text;

    public bool AtEnd => _pos >= _text.Length;

    private char Current => _text[_pos];

    public void SkipWhitespace()
    {
        while (_pos < _text.Length && char.IsWhiteSpace(_text[_pos]))
            _pos++;
    }

    public JsonValue ParseValue()
    {
        SkipWhitespace();
        if (AtEnd)
            throw new FormatException("JSON 意外结束");
        return Current switch
        {
            '{' => ParseObject(),
            '[' => ParseArray(),
            '"' => new JsonString(ParseString()),
            _ => throw new FormatException($"不支持的 JSON 值(位置 {_pos}):仅支持对象/数组/字符串"),
        };
    }

    private JsonObject ParseObject()
    {
        _pos++; // '{'
        var obj = new JsonObject();
        SkipWhitespace();
        if (!AtEnd && Current == '}')
        {
            _pos++;
            return obj;
        }

        while (true)
        {
            SkipWhitespace();
            if (AtEnd || Current != '"')
                throw new FormatException("JSON 对象缺少键名引号");
            string key = ParseString();
            SkipWhitespace();
            if (AtEnd || Current != ':')
                throw new FormatException("JSON 对象缺少冒号");
            _pos++;
            obj.Add(key, ParseValue());
            SkipWhitespace();
            if (AtEnd)
                throw new FormatException("JSON 对象未闭合");
            if (Current == ',')
            {
                _pos++;
                continue;
            }
            if (Current == '}')
            {
                _pos++;
                return obj;
            }
            throw new FormatException("JSON 对象缺少逗号或右花括号");
        }
    }

    private JsonArray ParseArray()
    {
        _pos++; // '['
        var arr = new JsonArray();
        SkipWhitespace();
        if (!AtEnd && Current == ']')
        {
            _pos++;
            return arr;
        }

        while (true)
        {
            arr.Items.Add(ParseValue());
            SkipWhitespace();
            if (AtEnd)
                throw new FormatException("JSON 数组未闭合");
            if (Current == ',')
            {
                _pos++;
                continue;
            }
            if (Current == ']')
            {
                _pos++;
                return arr;
            }
            throw new FormatException("JSON 数组缺少逗号或右方括号");
        }
    }

    private string ParseString()
    {
        _pos++; // 开引号
        var sb = new StringBuilder();
        while (true)
        {
            if (AtEnd)
                throw new FormatException("JSON 字符串未闭合");
            char c = Current;
            if (c == '"')
            {
                _pos++;
                return sb.ToString();
            }
            if (c == '\\')
            {
                _pos++;
                if (AtEnd)
                    throw new FormatException("JSON 字符串转义未完成");
                char e = Current;
                switch (e)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (_pos + 4 >= _text.Length)
                            throw new FormatException("JSON \\u 转义不完整");
                        sb.Append((char)Convert.ToInt32(_text.Substring(_pos + 1, 4), 16));
                        _pos += 4;
                        break;
                    default:
                        throw new FormatException($"未知 JSON 转义 \\{e}");
                }
                _pos++;
            }
            else
            {
                sb.Append(c);
                _pos++;
            }
        }
    }
}
