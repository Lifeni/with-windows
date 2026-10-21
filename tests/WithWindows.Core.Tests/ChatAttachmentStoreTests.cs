using WithWindows.Core.Chat;

namespace WithWindows.Core.Tests;

public class ChatAttachmentStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ww-attachments-" + Guid.NewGuid().ToString("N"));

    public ChatAttachmentStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void SaveLoadDelete_往返一致()
    {
        var store = new ChatAttachmentStore(_dir);
        ChatAttachment attachment = store.Save(
            [1, 2, 3, 4],
            "截图.png",
            "image/png",
            320,
            200);

        Assert.Equal([1, 2, 3, 4], store.LoadBytes(attachment));
        Assert.Equal("截图.png", attachment.DisplayName);
        Assert.Equal(320, attachment.Width);
        Assert.Equal(200, attachment.Height);

        store.Delete(attachment);
        Assert.False(File.Exists(Path.Combine(_dir, attachment.Id + ".png")));
    }

    [Fact]
    public void Save_超过1MiB_拒绝()
    {
        var store = new ChatAttachmentStore(_dir);
        byte[] tooLarge = new byte[ChatAttachmentStore.MaxBytes + 1];

        Assert.Throws<ArgumentException>(() =>
            store.Save(tooLarge, "大图.png", "image/png", 1, 1));
    }

    [Fact]
    public void Clear_删除全部附件()
    {
        var store = new ChatAttachmentStore(_dir);
        store.Save([1], "一.png", "image/png", 1, 1);
        store.Save([2], "二.jpg", "image/jpeg", 1, 1);

        store.Clear();

        Assert.Empty(Directory.EnumerateFiles(_dir));
    }
}
