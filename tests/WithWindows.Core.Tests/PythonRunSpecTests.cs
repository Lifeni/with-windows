using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class PythonRunSpecTests
{
    [Fact]
    public void RoundTrip_保留解释器与脚本路径()
    {
        var spec = new PythonRunSpec(
            @"C:\Python314\python.exe",
            @"C:\Users\User\脚本.py",
            @"C:\Users\User");

        PythonRunSpec? parsed = PythonRunSpec.TryParse(spec.Serialize());

        Assert.NotNull(parsed);
        Assert.Equal(spec.PythonPath, parsed.PythonPath);
        Assert.Equal(spec.ScriptPath, parsed.ScriptPath);
        Assert.Equal(spec.WorkingDirectory, parsed.WorkingDirectory);
    }

    [Fact]
    public void TryParse_拒绝相对路径()
    {
        const string json =
            """{"pythonPath":"python.exe","scriptPath":"C:\\script.py","workingDirectory":"C:\\"}""";

        Assert.Null(PythonRunSpec.TryParse(json));
    }
}
