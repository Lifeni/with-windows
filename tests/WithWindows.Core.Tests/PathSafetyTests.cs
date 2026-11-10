using WithWindows.Core.Diagnostics;

namespace WithWindows.Core.Tests;

public sealed class PathSafetyTests
{
    [Fact]
    public void IsWithinDirectory_子文件返回真()
    {
        Assert.True(PathSafety.IsWithinDirectory(
            @"C:\AppData\logs\task\run.log",
            @"C:\AppData\logs"));
    }

    [Fact]
    public void IsWithinDirectory_父级跳转返回假()
    {
        Assert.False(PathSafety.IsWithinDirectory(
            @"C:\AppData\logs\..\secret.txt",
            @"C:\AppData\logs"));
    }

    [Fact]
    public void IsWithinDirectory_绝对路径返回假()
    {
        Assert.False(PathSafety.IsWithinDirectory(
            @"D:\secret.txt",
            @"C:\AppData\logs"));
    }
}
