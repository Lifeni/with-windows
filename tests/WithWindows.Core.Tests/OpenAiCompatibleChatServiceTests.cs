using System.Net;
using System.Text;
using WithWindows.Core.Chat;
using WithWindows.Core.Config;

namespace WithWindows.Core.Tests;

public class OpenAiCompatibleChatServiceTests
{
    [Fact]
    public async Task StreamAsync_解析SSE到完成标记()
    {
        const string sse =
            "data: {\"choices\":[{\"delta\":{\"content\":\"你\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{\"content\":\"好\"}}]}\n\n" +
            "data: [DONE]\n\n";
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(sse, Encoding.UTF8, "text/event-stream"),
        };
        using var http = new HttpClient(new StubHandler(response));
        var service = new OpenAiCompatibleChatService(http);
        var settings = new ChatSettings
        {
            BaseUrl = "https://example.test/v1",
            Model = "demo",
        };

        var text = new StringBuilder();
        await foreach (ChatDelta delta in service.StreamAsync(
            settings,
            "key",
            [new ChatMessage(ChatRole.User, "你好")],
            CancellationToken.None))
        {
            text.Append(delta.Content);
        }

        Assert.Equal("你好", text.ToString());
    }

    [Fact]
    public async Task StreamAsync_兼容普通JSON响应()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """{"choices":[{"message":{"content":"完整回答"}}]}""",
                Encoding.UTF8,
                "application/json"),
        };
        using var http = new HttpClient(new StubHandler(response));
        var service = new OpenAiCompatibleChatService(http);

        var chunks = new List<string>();
        await foreach (ChatDelta delta in service.StreamAsync(
            new ChatSettings { BaseUrl = "https://example.test/v1", Model = "demo" },
            "",
            [new ChatMessage(ChatRole.User, "问题")],
            CancellationToken.None))
        {
            chunks.Add(delta.Content);
        }

        Assert.Equal(["完整回答"], chunks);
    }

    [Fact]
    public async Task StreamAsync_TextPlain响应仍按SSE解析()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                "data: {\"choices\":[{\"delta\":{\"content\":\"兼容\"}}]}\n\n" +
                "data: [DONE]\n\n",
                Encoding.UTF8,
                "text/plain"),
        };
        using var http = new HttpClient(new StubHandler(response));
        var service = new OpenAiCompatibleChatService(http);

        var text = new StringBuilder();
        await foreach (ChatDelta delta in service.StreamAsync(
            new ChatSettings { BaseUrl = "https://example.test/v1", Model = "demo" },
            "",
            [new ChatMessage(ChatRole.User, "问题")],
            CancellationToken.None))
        {
            text.Append(delta.Content);
        }

        Assert.Equal("兼容", text.ToString());
    }

    [Fact]
    public async Task StreamAsync_401_抛出可显示错误()
    {
        var response = new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            Content = new StringContent(
                """{"error":{"message":"invalid key"}}""",
                Encoding.UTF8,
                "application/json"),
        };
        using var http = new HttpClient(new StubHandler(response));
        var service = new OpenAiCompatibleChatService(http);

        ChatRequestException ex = await Assert.ThrowsAsync<ChatRequestException>(async () =>
        {
            await foreach (ChatDelta _ in service.StreamAsync(
                new ChatSettings
                {
                    BaseUrl = "https://example.test/v1",
                    Model = "demo",
                },
                "bad",
                [new ChatMessage(ChatRole.User, "问题")],
                CancellationToken.None))
            {
            }
        });

        Assert.Equal(HttpStatusCode.Unauthorized, ex.StatusCode);
        Assert.Contains("API Key", ex.UserMessage);
    }

    [Fact]
    public async Task StreamAsync_带图片_请求包含DataUrl()
    {
        string directory =
            Path.Combine(Path.GetTempPath(), "ww-chat-service-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var store = new ChatAttachmentStore(directory);
            ChatAttachment attachment = store.Save(
                [4, 5, 6],
                "图片.png",
                "image/png",
                1,
                1);
            string? requestBody = null;
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "data: [DONE]\n\n",
                    Encoding.UTF8,
                    "text/event-stream"),
            };
            using var http = new HttpClient(new StubHandler(
                response,
                requestJson => requestBody = requestJson));
            var service = new OpenAiCompatibleChatService(http, store);

            await foreach (ChatDelta _ in service.StreamAsync(
                new ChatSettings
                {
                    BaseUrl = "https://example.test/v1",
                    Model = "vision-model",
                },
                "",
                [
                    new ChatMessage(ChatRole.User, "看图")
                    {
                        Attachments = [attachment],
                    },
                ],
                CancellationToken.None))
            {
            }

            Assert.NotNull(requestBody);
            Assert.Contains("\"type\":\"image_url\"", requestBody);
            Assert.Contains("data:image/png;base64,BAUG", requestBody);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class StubHandler(
        HttpResponseMessage response,
        Action<string>? onRequest = null) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (onRequest is not null && request.Content is not null)
            {
                onRequest(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            return response;
        }
    }
}
