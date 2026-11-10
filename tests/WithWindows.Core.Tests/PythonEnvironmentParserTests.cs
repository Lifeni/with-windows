using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class PythonEnvironmentParserTests
{
    [Fact]
    public void ParseProbeJson_返回Python3路径版本和位数()
    {
        const string json =
            """{"executable":"C:\\Python314\\python.exe","major":3,"minor":14,"micro":7,"bits":64}""";

        PythonEnvironmentSnapshot? environment =
            PythonEnvironmentParser.ParseProbeJson(
                json,
                PythonInterpreterSource.Path,
                packageFamilyName: null);

        Assert.NotNull(environment);
        Assert.Equal(@"C:\Python314\python.exe", environment.Path);
        Assert.Equal("3.14.7", environment.Version);
        Assert.Equal(64, environment.Bits);
        Assert.Equal(PythonInterpreterSource.Path, environment.Source);
    }

    [Fact]
    public void DisplayText_包含版本架构来源和路径()
    {
        var environment = new PythonEnvironmentSnapshot(
            @"C:\Python314\python.exe",
            "3.14.7",
            64,
            PythonInterpreterSource.Manual,
            packageFamilyName: null);

        Assert.Equal(
            @"Python 3.14.7・x64・手动选择・C:\Python314\python.exe",
            environment.DisplayText);
    }

    [Fact]
    public void ParseProbeJson_拒绝Python2()
    {
        const string json =
            """{"executable":"C:\\Python27\\python.exe","major":2,"minor":7,"micro":18,"bits":64}""";

        Assert.Null(PythonEnvironmentParser.ParseProbeJson(
            json,
            PythonInterpreterSource.Path,
            packageFamilyName: null));
    }

    [Theory]
    [InlineData("""{"executable":"python.exe","major":3,"minor":13,"micro":0,"bits":64}""")]
    [InlineData("""{"executable":"C:\\Python313\\python.exe","major":3,"minor":13,"micro":0,"bits":12}""")]
    [InlineData("{ 坏")]
    public void ParseProbeJson_拒绝无效输出(string json)
    {
        Assert.Null(PythonEnvironmentParser.ParseProbeJson(
            json,
            PythonInterpreterSource.Path,
            packageFamilyName: null));
    }

    [Fact]
    public void ParsePyLauncherPaths_只提取绝对exe路径()
    {
        const string output =
            """
             -V:3.14 *        C:\Users\User\AppData\Local\Programs\Python\Python314\python.exe
             -V:3.12          C:\Python312\python.exe
             not a path
            """;

        IReadOnlyList<string> paths = PythonEnvironmentParser.ParsePyLauncherPaths(output);

        Assert.Equal(
            [
                @"C:\Users\User\AppData\Local\Programs\Python\Python314\python.exe",
                @"C:\Python312\python.exe",
            ],
            paths);
    }

    [Theory]
    [InlineData(@"C:\Users\User\AppData\Local\Microsoft\WindowsApps\python.exe")]
    [InlineData(@"C:\Users\User\AppData\Local\Microsoft\WindowsApps\python3.exe")]
    public void IsStoreAliasPath_识别WindowsApps路径(string path)
        => Assert.True(PythonEnvironmentParser.IsStoreAliasPath(path));

    [Fact]
    public void IsStoreAliasPath_普通解释器不算Store别名()
        => Assert.False(PythonEnvironmentParser.IsStoreAliasPath(@"C:\Python314\python.exe"));
}
