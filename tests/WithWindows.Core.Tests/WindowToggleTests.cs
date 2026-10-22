using WithWindows.Core.App;
using WithWindows.Core.Interop;

namespace WithWindows.Core.Tests;

public class WindowToggleTests
{
    [Fact]
    public void Decide_隐藏的窗口_显示()
        => Assert.Equal(WindowToggleAction.Show, WindowToggle.Decide(isVisible: false, isActive: false, isMinimized: false));

    [Fact]
    public void Decide_前台可见的窗口_隐藏()
        => Assert.Equal(WindowToggleAction.Hide, WindowToggle.Decide(isVisible: true, isActive: true, isMinimized: false));

    [Fact]
    public void Decide_可见但被别的窗口挡住_前置()
        => Assert.Equal(WindowToggleAction.Activate, WindowToggle.Decide(isVisible: true, isActive: false, isMinimized: false));

    [Fact]
    public void Decide_最小化的窗口_显示()
        => Assert.Equal(WindowToggleAction.Show, WindowToggle.Decide(isVisible: true, isActive: false, isMinimized: true));
}

public class WindowInteropTests
{
    [Fact]
    public void IsForeground_空句柄_恒为假()
        => Assert.False(WindowInterop.IsForeground(IntPtr.Zero));

    [Fact]
    public void IsVisible_空句柄_恒为假()
        => Assert.False(WindowInterop.IsVisible(IntPtr.Zero));

    [Fact]
    public void IsMinimized_空句柄_恒为假()
        => Assert.False(WindowInterop.IsMinimized(IntPtr.Zero));

    [Theory]
    [InlineData(100, 200, 0x00C80064)]
    [InlineData(-1920, 200, 0x00C8F880)]
    [InlineData(-1, -1, -1)]
    public void BuildMouseMessageLParam_按高低字打包屏幕坐标(int x, int y, int expected)
        => Assert.Equal((nint)expected, WindowInterop.BuildMouseMessageLParam(x, y));
}
