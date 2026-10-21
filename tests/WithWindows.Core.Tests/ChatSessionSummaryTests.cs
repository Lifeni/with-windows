using WithWindows.Core.Chat;

namespace WithWindows.Core.Tests;

public class ChatSessionSummaryTests
{
    [Fact]
    public void Create_使用首条用户消息()
    {
        ChatMessage[] messages =
        [
            new(ChatRole.Assistant, "你好"),
            new(ChatRole.User, "帮我整理一份很长的项目计划"),
        ];

        Assert.Equal(
            "帮我整理一份很长的项目计划",
            ChatSessionSummary.Create(messages));
    }

    [Fact]
    public void Create_纯图片消息_返回图片对话()
    {
        var message = new ChatMessage(ChatRole.User, "")
        {
            Attachments =
            [
                new ChatAttachment("id", "图片.png", "image/png", 1, 1, 1),
            ],
        };

        Assert.Equal("图片对话", ChatSessionSummary.Create([message]));
    }

    [Fact]
    public void Create_空会话_返回AI对话()
    {
        Assert.Equal("AI 对话", ChatSessionSummary.Create([]));
    }

    [Fact]
    public void Create_多行消息_合并为单行标题()
    {
        ChatMessage[] messages =
        [
            new(ChatRole.User, "第一行\r\n\n 第二行"),
        ];

        Assert.Equal("第一行 第二行", ChatSessionSummary.Create(messages));
    }

    [Fact]
    public void Create_超长消息_截断并追加省略号()
    {
        ChatMessage[] messages =
        [
            new(ChatRole.User, new string('长', 40)),
        ];

        Assert.Equal(new string('长', 36) + "…", ChatSessionSummary.Create(messages));
    }

    [Fact]
    public void Create_截断边界包含Emoji_不会切断代理对()
    {
        ChatMessage[] messages =
        [
            new(ChatRole.User, new string('a', 35) + "😀" + "tail"),
        ];

        Assert.Equal(
            new string('a', 35) + "😀…",
            ChatSessionSummary.Create(messages));
    }

    [Fact]
    public void Create_截断边界包含家庭Emoji_不会拆开扩展字素簇()
    {
        const string familyEmoji = "👨‍👩‍👧‍👦";
        ChatMessage[] messages =
        [
            new(ChatRole.User, new string('a', 35) + familyEmoji + "tail"),
        ];

        Assert.Equal(
            new string('a', 35) + familyEmoji + "…",
            ChatSessionSummary.Create(messages));
    }
}
