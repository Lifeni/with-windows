using WithWindows.Core.App;

namespace WithWindows.Core.Tests;

public class WindowRecoveryPolicyTests
{
    [Theory]
    [InlineData(true, 2, true, WindowRecoveryStep.Stop)]
    [InlineData(false, 2, true, WindowRecoveryStep.Wait)]
    [InlineData(false, 0, true, WindowRecoveryStep.Restart)]
    [InlineData(false, 0, false, WindowRecoveryStep.GiveUp)]
    [InlineData(false, 2, false, WindowRecoveryStep.Wait)]
    public void DecideShowRecovery_按挂载与剩余次数决定下一步(
        bool pageLoaded,
        int remainingChecks,
        bool hasPageType,
        WindowRecoveryStep expected)
    {
        Assert.Equal(expected, WindowRecoveryPolicy.DecideShowRecovery(
            pageLoaded,
            remainingChecks,
            hasPageType));
    }

    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, true, true, false)]
    public void CanNavigatePage_只允许初始导航或仍在可视化树中的页面(
        bool hasCurrentPage,
        bool pageLoaded,
        bool hasXamlRoot,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowRecoveryPolicy.CanNavigatePage(
                hasCurrentPage,
                pageLoaded,
                hasXamlRoot));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    public void CanRestartRecovery_最多自动重启一次(int restartCount, bool expected)
    {
        Assert.Equal(
            expected,
            WindowRecoveryPolicy.CanRestartRecovery(restartCount));
    }

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, false)]
    public void IsExpectedPageReady_必须同时匹配页面类型并仍在可视化树中(
        bool typeMatches,
        bool pageLoaded,
        bool hasXamlRoot,
        bool expected)
    {
        Assert.Equal(
            expected,
            WindowRecoveryPolicy.IsExpectedPageReady(
                typeMatches,
                pageLoaded,
                hasXamlRoot));
    }
}
