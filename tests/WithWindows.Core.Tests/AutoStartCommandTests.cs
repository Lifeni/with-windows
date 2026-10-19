using WithWindows.Core.App;

namespace WithWindows.Core.Tests;

public class AutoStartCommandTests
{
    private const string ExePath = @"C:\Users\With User\AppData\Local\Programs\With Windows\WithWindows.exe";
    private const string SimpleExePath = @"C:\Tools\WithWindows.exe";

    [Fact]
    public void Build_路径加引号防空格截断_并带隐藏启动参数()
        => Assert.Equal($"\"{ExePath}\" --tray", AutoStartCommand.Build(ExePath));

    [Fact]
    public void IsTrayLaunch_命令行里有隐藏启动参数_为真()
        => Assert.True(AutoStartCommand.IsTrayLaunch([@"C:\WithWindows.exe", "--tray"]));

    [Fact]
    public void IsTrayLaunch_大小写不同也算_为真()
        => Assert.True(AutoStartCommand.IsTrayLaunch([@"C:\WithWindows.exe", "--TRAY"]));

    [Fact]
    public void IsTrayLaunch_手动启动没有参数_为假()
        => Assert.False(AutoStartCommand.IsTrayLaunch([@"C:\WithWindows.exe"]));

    [Fact]
    public void IsTrayLaunch_只认完整参数_不把别的参数里的子串当命中()
        => Assert.False(AutoStartCommand.IsTrayLaunch([@"C:\WithWindows.exe", "--tray-preview"]));

    [Fact]
    public void IsCurrent_与当前形式一致_为真()
        => Assert.True(AutoStartCommand.IsCurrent($"\"{ExePath}\" --tray", ExePath));

    [Fact]
    public void IsCurrent_大小写与首尾空白不同_仍为真()
        => Assert.True(AutoStartCommand.IsCurrent($"  \"{ExePath.ToUpperInvariant()}\" --TRAY  ", ExePath));

    [Fact]
    public void IsCurrent_旧版本的裸路径_为假()
        => Assert.False(AutoStartCommand.IsCurrent($"\"{ExePath}\"", ExePath));

    [Fact]
    public void IsCurrent_指向别的程序_为假()
        => Assert.False(AutoStartCommand.IsCurrent("\"C:\\Other\\WithWindows.exe\" --tray", ExePath));

    [Fact]
    public void IsCurrent_当前程序路径未知_为假()
        => Assert.False(AutoStartCommand.IsCurrent($"\"{ExePath}\" --tray", null));

    [Fact]
    public void PointsAt_旧版本的裸路径_为真()
        => Assert.True(AutoStartCommand.PointsAt($"\"{ExePath}\"", ExePath));

    [Fact]
    public void PointsAt_带其它启动参数_为真()
        => Assert.True(AutoStartCommand.PointsAt($"\"{ExePath}\" --tray", ExePath));

    [Fact]
    public void PointsAt_没加引号但路径不含空格_为真()
        => Assert.True(AutoStartCommand.PointsAt(SimpleExePath, SimpleExePath));

    [Fact]
    public void PointsAt_没加引号且路径含空格_没法还原_为假()
        => Assert.False(AutoStartCommand.PointsAt(ExePath, ExePath));

    [Fact]
    public void PointsAt_指向别的程序_为假()
        => Assert.False(AutoStartCommand.PointsAt("\"C:\\Other\\WithWindows.exe\"", ExePath));

    [Fact]
    public void PointsAt_注册表里没有这一项_为假()
        => Assert.False(AutoStartCommand.PointsAt(null, ExePath));

    [Fact]
    public void ExecutableOf_引号没闭合_返回空()
        => Assert.Null(AutoStartCommand.ExecutableOf("\"C:\\WithWindows.exe"));

    [Fact]
    public void ExecutableOf_空白_返回空()
        => Assert.Null(AutoStartCommand.ExecutableOf("   "));
}
