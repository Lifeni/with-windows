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
    public async Task Create_新配置默认携带系统提示()
    {
        var settings = new ChatSettings
        {
            BaseUrl = "https://example.test/v1",
            Model = "demo",
        };

        using HttpRequestMessage request = ChatRequestFactory.Create(
            settings,
            "",
            [new ChatMessage(ChatRole.User, "你好")]);

        string json = await request.Content!.ReadAsStringAsync();
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement systemMessage = document.RootElement.GetProperty("messages")[0];
        Assert.Equal("system", systemMessage.GetProperty("role").GetString());
        Assert.Equal(
            ChatSettings.DefaultSystemPrompt,
            systemMessage.GetProperty("content").GetString());
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
                    SystemPrompt = "",
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
    public async Task Create_历史图片只保留预算内最新四张()
    {
        string directory =
            Path.Combine(Path.GetTempPath(), "ww-chat-budget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ChatAttachmentStore(directory);
            ChatMessage[] messages = Enumerable.Range(0, 5)
                .Select(index =>
                {
                    ChatAttachment attachment = store.Save(
                        [(byte)index],
                        $"图片{index}.png",
                        "image/png",
                        width: 1,
                        height: 1);
                    return new ChatMessage(ChatRole.User, $"消息{index}")
                    {
                        Attachments = [attachment],
                    };
                })
                .ToArray();

            using HttpRequestMessage request = ChatRequestFactory.Create(
                new ChatSettings
                {
                    BaseUrl = "https://example.test/v1",
                    Model = "vision-model",
                    SystemPrompt = "",
                },
                "",
                messages,
                store);

            string json = await request.Content!.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement requestMessages =
                document.RootElement.GetProperty("messages");
            Assert.Equal(5, requestMessages.GetArrayLength());
            Assert.Equal(
                JsonValueKind.String,
                requestMessages[0].GetProperty("content").ValueKind);

            int imageParts = requestMessages.EnumerateArray()
                .Where(message => message.GetProperty("content").ValueKind == JsonValueKind.Array)
                .SelectMany(message => message.GetProperty("content").EnumerateArray())
                .Count(part => part.GetProperty("type").GetString() == "image_url");
            Assert.Equal(ChatHistoryImageBudget.MaxImages, imageParts);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Create_实际文件超过单图上限_跳过历史图片()
    {
        string directory =
            Path.Combine(Path.GetTempPath(), "ww-chat-actual-size-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ChatAttachmentStore(directory);
            ChatAttachment attachment = store.Save(
                [1],
                "过期元数据.png",
                "image/png",
                width: 1,
                height: 1);
            string path = Path.Combine(directory, attachment.Id + ".png");
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write))
                stream.Write(new byte[ChatAttachmentStore.MaxBytes], 0, ChatAttachmentStore.MaxBytes);

            using HttpRequestMessage request = ChatRequestFactory.Create(
                new ChatSettings
                {
                    BaseUrl = "https://example.test/v1",
                    Model = "vision-model",
                    SystemPrompt = "",
                },
                "",
                [
                    new ChatMessage(ChatRole.User, "只保留文字")
                    {
                        Attachments = [attachment],
                    },
                ],
                store);

            string json = await request.Content!.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement content = document.RootElement.GetProperty("messages")[0]
                .GetProperty("content");
            Assert.Equal(JsonValueKind.String, content.ValueKind);
            Assert.Equal("只保留文字", content.GetString());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Create_纯图片消息被实际字节校验裁剪_保留用户占位()
    {
        string directory =
            Path.Combine(Path.GetTempPath(), "ww-chat-actual-image-only-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ChatAttachmentStore(directory);
            ChatAttachment attachment = store.Save(
                [1],
                "过期元数据.png",
                "image/png",
                width: 1,
                height: 1);
            string path = Path.Combine(directory, attachment.Id + ".png");
            using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write))
                stream.Write(new byte[ChatAttachmentStore.MaxBytes], 0, ChatAttachmentStore.MaxBytes);

            using HttpRequestMessage request = ChatRequestFactory.Create(
                new ChatSettings
                {
                    BaseUrl = "https://example.test/v1",
                    Model = "vision-model",
                    SystemPrompt = "",
                },
                "",
                [
                    new ChatMessage(ChatRole.User, "")
                    {
                        Attachments = [attachment],
                    },
                    new ChatMessage(ChatRole.Assistant, "旧回复"),
                ],
                store);

            string json = await request.Content!.ReadAsStringAsync();
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement messages = document.RootElement.GetProperty("messages");
            Assert.Equal(2, messages.GetArrayLength());
            Assert.Equal("user", messages[0].GetProperty("role").GetString());
            Assert.Equal(
                ChatHistoryImageBudget.OmittedImagePlaceholder,
                messages[0].GetProperty("content").GetString());
            Assert.Equal("assistant", messages[1].GetProperty("role").GetString());
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
