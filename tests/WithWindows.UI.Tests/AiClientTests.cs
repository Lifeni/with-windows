using WithWindows.Core;

namespace WithWindows.Tests;

public class AiClientTests
{
    [Fact]
    public void ParseSseDelta_ReturnsContent()
    {
        Assert.Equal("你好", AiClient.ParseSseDelta("""{"choices":[{"delta":{"content":"你好"}}]}"""));
    }

    [Fact]
    public void ParseSseDelta_EmptyContent_ReturnsEmptyString()
    {
        Assert.Equal("", AiClient.ParseSseDelta("""{"choices":[{"delta":{"content":""}}]}"""));
    }

    [Fact]
    public void ParseSseDelta_NoChoices_ReturnsNull()
    {
        Assert.Null(AiClient.ParseSseDelta("""{"choices":[]}"""));
    }

    [Fact]
    public void ParseSseDelta_NoDelta_ReturnsNull()
    {
        Assert.Null(AiClient.ParseSseDelta("""{"choices":[{"message":{"content":"x"}}]}"""));
    }

    [Fact]
    public void ParseSseDelta_ReasoningModel_ContentNull_ReturnsNull()
    {
        // 推理模型可能只发 reasoning_content；content 为 null 时跳过
        Assert.Null(AiClient.ParseSseDelta("""{"choices":[{"delta":{"reasoning_content":"思考中"}}]}"""));
    }

    [Fact]
    public void ParseSseDelta_InvalidJson_Throws()
    {
        Assert.ThrowsAny<Exception>(() => AiClient.ParseSseDelta("{ not json"));
    }
}
