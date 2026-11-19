using WithWindows.Core.Theme;

namespace WithWindows.Core.Tests;

public sealed class WallpaperStoreTests : IDisposable
{
    private readonly string _root =
        Path.Combine(
            Path.GetTempPath(),
            "ww-wallpaper-" + Guid.NewGuid().ToString("N"));
    private readonly string _sourceDirectory;
    private readonly string _wallpaperDirectory;

    public WallpaperStoreTests()
    {
        _sourceDirectory = Path.Combine(_root, "source");
        _wallpaperDirectory = Path.Combine(_root, "wallpapers");
        Directory.CreateDirectory(_sourceDirectory);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void Import_复制到应用目录并保留扩展名()
    {
        string source = Path.Combine(_sourceDirectory, "photo.PNG");
        File.WriteAllText(source, "image");

        string imported = WallpaperStore.Import(
            source,
            _wallpaperDirectory,
            "light");

        Assert.StartsWith(
            Path.GetFullPath(_wallpaperDirectory),
            Path.GetFullPath(imported),
            StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".png", imported, StringComparison.Ordinal);
        Assert.Equal("image", File.ReadAllText(imported));
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void Import_不支持的格式_抛异常()
    {
        string source = Path.Combine(_sourceDirectory, "wallpaper.svg");
        File.WriteAllText(source, "<svg/>");

        Assert.Throws<ArgumentException>(() =>
            WallpaperStore.Import(source, _wallpaperDirectory, "dark"));
    }

    [Fact]
    public void DeleteOwned_只删除应用目录内的文件()
    {
        string owned = WallpaperStore.Import(
            CreateSource("owned.jpg"),
            _wallpaperDirectory,
            "light");
        string outside = Path.Combine(_sourceDirectory, "outside.jpg");
        File.WriteAllText(outside, "keep");

        WallpaperStore.DeleteOwned(owned, _wallpaperDirectory);
        WallpaperStore.DeleteOwned(outside, _wallpaperDirectory);

        Assert.False(File.Exists(owned));
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void ExistingOrNull_空值与缺失文件返回_null()
    {
        Assert.Null(WallpaperStore.ExistingOrNull(null));
        Assert.Null(WallpaperStore.ExistingOrNull(""));
        Assert.Null(WallpaperStore.ExistingOrNull(
            Path.Combine(_sourceDirectory, "missing.png")));
    }

    private string CreateSource(string name)
    {
        string path = Path.Combine(_sourceDirectory, name);
        File.WriteAllText(path, "image");
        return path;
    }
}
