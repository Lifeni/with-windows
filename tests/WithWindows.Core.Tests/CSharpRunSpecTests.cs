using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class CSharpRunSpecTests
{
    [Fact]
    public void RoundTrip_保留脚本路径和工作目录()
    {
        var spec = new CSharpRunSpec(
            @"C:\Users\User\AppData\Roaming\WithWindows\scheduled-tasks\scripts\task.csx",
            @"C:\Users\User");

        CSharpRunSpec? parsed = CSharpRunSpec.TryParse(spec.Serialize());

        Assert.NotNull(parsed);
        Assert.Equal(spec.ScriptPath, parsed.ScriptPath);
        Assert.Equal(spec.WorkingDirectory, parsed.WorkingDirectory);
    }

    [Fact]
    public void TryParse_拒绝相对路径()
    {
        const string json =
            """{"scriptPath":"script.csx","workingDirectory":"C:\\"}""";

        Assert.Null(CSharpRunSpec.TryParse(json));
    }
}
