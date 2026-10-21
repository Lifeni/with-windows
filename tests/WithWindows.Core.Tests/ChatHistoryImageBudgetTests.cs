using WithWindows.Core.Chat;

namespace WithWindows.Core.Tests;

public class ChatHistoryImageBudgetTests
{
    [Fact]
    public void Limit_按最新图片优先保留数量()
    {
        ChatMessage[] messages =
        [
            TextWithImages("较早", Image("old", 100)),
            TextWithImages("中间", Image("middle", 100)),
            TextWithImages("最新", Image("new", 100)),
        ];

        IReadOnlyList<ChatMessage> limited =
            ChatHistoryImageBudget.Limit(messages, maxImages: 2, maxBytes: 1024);

        Assert.Equal(["middle", "new"], limited.SelectMany(m => m.Attachments).Select(a => a.Id));
        Assert.Equal(["较早", "中间", "最新"], limited.Select(m => m.Content));
    }

    [Fact]
    public void Limit_按最新图片优先满足总字节预算()
    {
        ChatMessage[] messages =
        [
            TextWithImages("较早", Image("old", 700)),
            TextWithImages("中间", Image("middle", 700)),
            TextWithImages("最新", Image("new", 700)),
        ];

        IReadOnlyList<ChatMessage> limited =
            ChatHistoryImageBudget.Limit(messages, maxImages: 4, maxBytes: 1500);

        Assert.Equal(["middle", "new"], limited.SelectMany(m => m.Attachments).Select(a => a.Id));
    }

    [Fact]
    public void Limit_纯图片用户消息失去图片后保留文字占位()
    {
        ChatMessage[] messages =
        [
            new(ChatRole.User, "") { Attachments = [Image("old", 100)] },
            TextWithImages("最新", Image("new", 100)),
        ];

        IReadOnlyList<ChatMessage> limited =
            ChatHistoryImageBudget.Limit(messages, maxImages: 1, maxBytes: 1024);

        Assert.Equal(2, limited.Count);
        Assert.Equal("（较早的图片已省略）", limited[0].Content);
        Assert.Empty(limited[0].Attachments);
        Assert.Equal("最新", limited[1].Content);
        Assert.Equal("new", Assert.Single(limited[1].Attachments).Id);
    }

    [Fact]
    public void Limit_不修改原始消息附件()
    {
        ChatMessage original = TextWithImages("消息", Image("old", 100));

        _ = ChatHistoryImageBudget.Limit([original], maxImages: 0, maxBytes: 1024);

        Assert.Single(original.Attachments);
    }

    private static ChatMessage TextWithImages(string text, params ChatAttachment[] attachments)
        => new(ChatRole.User, text) { Attachments = [.. attachments] };

    private static ChatAttachment Image(string id, long bytes)
        => new(id, id + ".png", "image/png", bytes, 1, 1);
}
