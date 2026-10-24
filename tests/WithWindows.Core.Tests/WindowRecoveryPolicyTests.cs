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

    [Fact]
    public void ShouldApplyQueuedRemount_导航代次未变化时执行()
    {
        Assert.True(WindowRecoveryPolicy.ShouldApplyQueuedRemount(
            queuedGeneration: 7,
            currentGeneration: 7));
    }

    [Fact]
    public void ShouldApplyQueuedRemount_产生新导航后跳过过期重建()
    {
        Assert.False(WindowRecoveryPolicy.ShouldApplyQueuedRemount(
            queuedGeneration: 7,
            currentGeneration: 8));
    }

    [Fact]
    public void ShouldRemountAfterActivation_长时间停用且期间未导航时重建()
    {
        Assert.True(WindowRecoveryPolicy.ShouldRemountAfterActivation(
            Now,
            Now.AddMinutes(-10),
            TimeSpan.FromMinutes(10),
            navigationAtDeactivation: 4,
            currentNavigation: 4));
    }

    [Fact]
    public void ShouldRemountAfterActivation_停用期间已完成导航时不重建()
    {
        Assert.False(WindowRecoveryPolicy.ShouldRemountAfterActivation(
            Now,
            Now.AddMinutes(-10),
            TimeSpan.FromMinutes(10),
            navigationAtDeactivation: 4,
            currentNavigation: 5));
    }

    [Theory]
    [InlineData(true, 2, 2, true, WindowRecoveryStep.Stop)]
    [InlineData(false, 2, 2, true, WindowRecoveryStep.Wait)]
    [InlineData(false, 0, 2, true, WindowRecoveryStep.Navigate)]
    [InlineData(false, 0, 0, true, WindowRecoveryStep.GiveUp)]
    [InlineData(false, 0, 2, false, WindowRecoveryStep.GiveUp)]
    [InlineData(false, 2, 2, false, WindowRecoveryStep.Wait)]
    public void DecideShowRecovery_按挂载与剩余次数决定下一步(
        bool pageLoaded,
        int remainingChecks,
        int remainingNavigationAttempts,
        bool hasPageType,
        WindowRecoveryStep expected)
    {
        Assert.Equal(expected, WindowRecoveryPolicy.DecideShowRecovery(
            pageLoaded,
            remainingChecks,
            remainingNavigationAttempts,
            hasPageType));
    }
}
