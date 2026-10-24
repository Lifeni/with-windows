using WithWindows.Core.App;

namespace WithWindows.Core.Tests;

public class RestartCommandTests
{
    [Fact]
    public void GetWaitForProcessId_读取有效进程号()
        => Assert.Equal(
            4321,
            RestartCommand.GetWaitForProcessId(["--restart-after=4321", "--page=todo"]));

    [Theory]
    [InlineData("--restart-after=0")]
    [InlineData("--restart-after=-1")]
    [InlineData("--restart-after=abc")]
    [InlineData("--restart-after=")]
    public void GetWaitForProcessId_忽略非法值(string argument)
        => Assert.Null(RestartCommand.GetWaitForProcessId([argument]));

    [Fact]
    public void GetPageTag_读取页面标识()
        => Assert.Equal("ai_chat", RestartCommand.GetPageTag(["--page=ai_chat"]));

    [Fact]
    public void GetPageTag_缺少参数时返回空()
        => Assert.Null(RestartCommand.GetPageTag(["--restart-after=4321"]));

    [Fact]
    public void GetRestartCount_读取已有重启次数()
        => Assert.Equal(
            1,
            RestartCommand.GetRestartCount(["--restart-count=1", "--page=todo"]));

    [Theory]
    [InlineData("--restart-count=0")]
    [InlineData("--restart-count=-1")]
    [InlineData("--restart-count=abc")]
    [InlineData("--restart-count=")]
    public void GetRestartCount_非法值按首次启动处理(string argument)
        => Assert.Equal(0, RestartCommand.GetRestartCount([argument]));

    [Fact]
    public void GetReadyEventName_使用旧进程号区分交接实例()
        => Assert.Equal(
            @"Local\WithWindows.RestartReady.4321",
            RestartCommand.GetReadyEventName(4321));
}
