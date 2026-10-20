using WithWindows.Core.Chat;

namespace WithWindows.Core.Tests;

public class ChatHistoryStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ww-chat-" + Guid.NewGuid().ToString("N"));

    public ChatHistoryStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ChatHistoryStore NewStore()
        => new(Path.Combine(_dir, "current.json"));

    [Fact]
    public void SaveAndLoad_往返一致()
    {
        ChatHistoryStore store = NewStore();
        var session = new ChatSession
        {
            Messages =
            [
                new ChatMessage(ChatRole.User, "你好"),
                new ChatMessage(ChatRole.Assistant, "你好！"),
            ],
        };

        store.Save(session);
        ChatSession loaded = store.Load();

        Assert.Equal(2, loaded.Messages.Count);
        Assert.Equal("你好！", loaded.Messages[1].Content);
        Assert.True(File.Exists(store.Path));
    }

    [Fact]
    public void Load_损坏主文件_从备份恢复()
    {
        ChatHistoryStore store = NewStore();
        store.Save(new ChatSession
        {
            Messages = [new ChatMessage(ChatRole.User, "保留我")],
        });
        store.Save(new ChatSession());
        File.WriteAllText(store.Path, "{ 坏");

        ChatSession recovered = store.Load();

        Assert.Contains(recovered.Messages, message => message.Content == "保留我");
    }

    [Fact]
    public void Clear_删除主文件和备份()
    {
        ChatHistoryStore store = NewStore();
        store.Save(new ChatSession());
        store.Save(new ChatSession());

        store.Clear();

        Assert.False(File.Exists(store.Path));
        Assert.False(File.Exists(store.BackupPath));
    }
}
