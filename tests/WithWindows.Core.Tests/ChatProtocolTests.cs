using System.Text.Json;
using WithWindows.Core.Chat;
using WithWindows.Core.Config;

namespace WithWindows.Core.Tests;

public class ChatProtocolTests
{
    [Theory]
    [InlineData("https://example.test/v1/", "https://example.test/v1")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1")]
    public void NormalizeBaseUrl_去掉尾部斜杠(string input, string expected)
    {
        Assert.Equal(expected, ChatValidation.NormalizeBaseUrl(input));
    }

    [Theory]
    [InlineData("")]
    [InlineData("/v1")]
    [InlineData("ftp://example.test")]
    [InlineData("https://example.test/v1?token=secret")]
    [InlineData("https://example.test/v1#fragment")]
    public void NormalizeBaseUrl_拒绝非法地址(string input)
    {
        Assert.Throws<ArgumentException>(() => ChatValidation.NormalizeBaseUrl(input));
    }

    [Fact]
    public async Task Create_请求包含系统消息并只在有密钥时带认证()
    {
        var settings = new ChatSettings
        {
            BaseUrl = "https://example.test/v1/",
            Model = "demo",
            SystemPrompt = "回答简洁。",
        };
        ChatMessage[] messages =
        [
            new(ChatRole.User, "你好"),
        ];

        using HttpRequestMessage request =
            ChatRequestFactory.Create(settings, "secret", messages);

        Assert.Equal(
            "https://example.test/v1/chat/completions",
            request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("secret", request.Headers.Authorization.Parameter);

        string json = await request.Content!.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(json);
        JsonElement root = doc.RootElement;
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal("demo", root.GetProperty("model").GetString());
        Assert.Equal("system", root.GetProperty("messages")[0].GetProperty("role").GetString());
        Assert.Equal(
            "回答简洁。",
            root.GetProperty("messages")[0].GetProperty("content").GetString());
        Assert.Equal("user", root.GetProperty("messages")[1].GetProperty("role").GetString());

        using HttpRequestMessage noKey =
            ChatRequestFactory.Create(settings, "", messages);
        Assert.Null(noKey.Headers.Authorization);
    }

    [Fact]
    public async Task Create_带图片_生成image_url内容片段()
    {
        string directory =
            Path.Combine(Path.GetTempPath(), "ww-chat-request-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ChatAttachmentStore(directory);
            ChatAttachment attachment = store.Save(
                [1, 2, 3],
                "截图.png",
                "image/png",
                width: 1,
                height: 1);
            var message = new ChatMessage(ChatRole.User, "看看这张图")
            {
                Attachments = [attachment],
            };

            using HttpRequestMessage request = ChatRequestFactory.Create(
                new ChatSettings
                {
                    BaseUrl = "https://example.test/v1",
                    Model = "vision-model",
                },
                "",
                [message],
                store);

            string json = await request.Content!.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement content = document.RootElement.GetProperty("messages")[0]
                .GetProperty("content");
            Assert.Equal(JsonValueKind.Array, content.ValueKind);
            Assert.Equal("text", content[0].GetProperty("type").GetString());
            Assert.Equal("看看这张图", content[0].GetProperty("text").GetString());
            Assert.Equal("image_url", content[1].GetProperty("type").GetString());
            Assert.StartsWith(
                "data:image/png;base64,AQID",
                content[1].GetProperty("image_url").GetProperty("url").GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ParseSse_读取内容并识别完成和错误()
    {
        ChatStreamEvent content = ChatStreamParser.Parse(
            """{"choices":[{"delta":{"content":"你好"}}]}""");
        Assert.Equal(ChatStreamEventKind.Content, content.Kind);
        Assert.Equal("你好", content.Text);

        Assert.Equal(ChatStreamEventKind.Done, ChatStreamParser.Parse("[DONE]").Kind);

        ChatStreamEvent error = ChatStreamParser.Parse(
            """{"error":{"message":"bad key"}}""");
        Assert.Equal(ChatStreamEventKind.Error, error.Kind);
        Assert.Equal("bad key", error.Text);

        ChatStreamEvent malformed = ChatStreamParser.Parse("{ 坏");
        Assert.Equal(ChatStreamEventKind.Error, malformed.Kind);
        Assert.Contains("无法解析", malformed.Text);
    }

    [Fact]
    public void ParseCompletion_兼容非流式JSON()
    {
        ChatStreamEvent result = ChatStreamParser.ParseCompletion(
            """{"choices":[{"message":{"role":"assistant","content":"完整回答"}}]}""");
        Assert.Equal(ChatStreamEventKind.Content, result.Kind);
        Assert.Equal("完整回答", result.Text);
    }
}
