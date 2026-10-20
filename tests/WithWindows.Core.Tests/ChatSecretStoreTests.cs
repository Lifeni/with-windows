using WithWindows.Core.Chat;

namespace WithWindows.Core.Tests;

public class ChatSecretStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "ww-secret-" + Guid.NewGuid().ToString("N"));

    private string PathName => Path.Combine(_dir, "credential.bin");

    public ChatSecretStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task SaveLoad_往返一致且磁盘不含明文()
    {
        var store = new ChatSecretStore(PathName);
        await store.SaveAsync("sk-test-secret");

        Assert.Equal("sk-test-secret", await store.LoadAsync());
        Assert.DoesNotContain("sk-test-secret", File.ReadAllText(PathName));
    }

    [Fact]
    public async Task Clear_删除文件()
    {
        var store = new ChatSecretStore(PathName);
        await store.SaveAsync("secret");
        store.Clear();

        Assert.Null(await store.LoadAsync());
    }

    [Fact]
    public async Task Load_损坏数据_抛出异常()
    {
        File.WriteAllBytes(PathName, [1, 2, 3, 4]);
        var store = new ChatSecretStore(PathName);

        await Assert.ThrowsAnyAsync<Exception>(() => store.LoadAsync());
    }
}
