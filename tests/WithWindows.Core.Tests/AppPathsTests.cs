using WithWindows.Core.Diagnostics;

namespace WithWindows.Core.Tests;

public class AppPathsTests
{
    [Fact]
    public void DataRoot_位于_APPDATA_下的_WithWindows()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        Assert.StartsWith(appData, AppPaths.DataRoot, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("WithWindows", AppPaths.DataRoot, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 各路径都位于_DataRoot_之下()
    {
        Assert.StartsWith(AppPaths.DataRoot, AppPaths.ConfigPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.DataRoot, AppPaths.LogPath, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(AppPaths.DataRoot, AppPaths.NotepadDraftPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void 各路径均非空且互不相同()
    {
        var all = new[] { AppPaths.DataRoot, AppPaths.ConfigPath, AppPaths.LogPath, AppPaths.NotepadDraftPath };

        Assert.All(all, p => Assert.False(string.IsNullOrWhiteSpace(p)));
        Assert.Equal(all.Length, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
}
