using WithWindows.Core.Scheduling;

namespace WithWindows.Core.Tests;

public sealed class LuaRunSpecTests
{
    [Fact]
    public void RoundTrip_保留脚本路径和工作目录()
    {
        var spec = new LuaRunSpec(
            @"C:\Users\User\AppData\Roaming\WithWindows\scheduled-tasks\scripts\task.lua",
            @"C:\Users\User");

        LuaRunSpec? parsed = LuaRunSpec.TryParse(spec.Serialize());

        Assert.NotNull(parsed);
        Assert.Equal(spec.ScriptPath, parsed.ScriptPath);
        Assert.Equal(spec.WorkingDirectory, parsed.WorkingDirectory);
    }

    [Fact]
    public void TryParse_拒绝相对路径()
    {
        const string json =
            """{"scriptPath":"script.lua","workingDirectory":"C:\\"}""";

        Assert.Null(LuaRunSpec.TryParse(json));
    }
}
