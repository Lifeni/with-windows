using WithWindows.Core.App;

namespace WithWindows.Core.Tests;

public class WindowRecoveryPolicyTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 10, 24, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ShouldRemount_短期停用不重载页面()
    {
        Assert.False(WindowRecoveryPolicy.ShouldRemount(
            Now,
            Now.AddMinutes(-9),
            TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ShouldRemount_达到阈值时重载页面()
    {
        Assert.True(WindowRecoveryPolicy.ShouldRemount(
            Now,
            Now.AddMinutes(-10),
            TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ShouldRemount_没有停用记录时不重载()
    {
        Assert.False(WindowRecoveryPolicy.ShouldRemount(
            Now,
            null,
            TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void ShouldRemount_系统时间倒退时不重载()
    {
        Assert.False(WindowRecoveryPolicy.ShouldRemount(
            Now,
            Now.AddMinutes(1),
            TimeSpan.FromMinutes(10)));
    }
}
