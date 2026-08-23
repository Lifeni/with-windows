using WithWindows.Config;

namespace WithWindows.Tests;

public class MiniJsonTests
{
    [Fact]
    public void Parse_ObjectWithStringFields_ReadsValues()
    {
        var root = MiniJson.Parse("""{ "hotkey": "F13", "action": "display_mode" }""") as JsonObject;

        Assert.NotNull(root);
        Assert.True(root!.TryGet("hotkey", out var hotkey));
        Assert.Equal("F13", ((JsonString)hotkey!).Value);
        Assert.True(root.TryGet("action", out var action));
        Assert.Equal("display_mode", ((JsonString)action!).Value);
    }

    [Fact]
    public void Parse_ArrayOfObjects_ReadsItems()
    {
        var root = MiniJson.Parse(
            """
            [
              { "hotkey": "F13", "action": "display_mode", "args": { "mode": "toggle", "modes": ["internal", "extend"] } }
            ]
            """) as JsonArray;

        Assert.NotNull(root);
        Assert.Single(root!.Items);
        var first = Assert.IsType<JsonObject>(root.Items[0]);
        Assert.True(first.TryGet("args", out var args));
        var argsObj = Assert.IsType<JsonObject>(args);
        Assert.True(argsObj.TryGet("modes", out var modes));
        var modesArr = Assert.IsType<JsonArray>(modes);
        Assert.Equal(2, modesArr.Items.Count);
        Assert.Equal("internal", ((JsonString)modesArr.Items[0]).Value);
        Assert.Equal("extend", ((JsonString)modesArr.Items[1]).Value);
    }

    [Fact]
    public void Parse_Escapes_AreDecoded()
    {
        var value = MiniJson.Parse("""" "a\"b\\c\u0041" """") as JsonString;

        Assert.NotNull(value);
        Assert.Equal("a\"b\\cA", value!.Value);
    }

    [Fact]
    public void Parse_Whitespace_IsTolerated()
    {
        var value = MiniJson.Parse("  { \"a\" : \"b\" }  ") as JsonObject;

        Assert.NotNull(value);
        Assert.True(value!.TryGet("a", out var a));
        Assert.Equal("b", ((JsonString)a!).Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("}")]
    [InlineData("[")]
    [InlineData("{\"a\"}")]
    [InlineData("{\"a\":1}")]
    [InlineData("true")]
    [InlineData("{\"a\":\"b\"}x")]
    [InlineData("\"unclosed")]
    public void Parse_InvalidJson_Throws(string text)
    {
        Assert.Throws<FormatException>(() => MiniJson.Parse(text));
    }
}
