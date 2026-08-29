using Microsoft.UI.Xaml;
using WithWindows.Core;

namespace WithWindows;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        bool smoke = Environment.GetCommandLineArgs().Contains("--smoke", StringComparer.OrdinalIgnoreCase);

        // 单实例守卫：常驻模式第二个实例直接退出；--smoke 不抢互斥体
        using var singleInstance = new SingleInstance();
        if (!smoke && !singleInstance.Owned)
        {
            Exit();
            return;
        }

        // 窗口构造内含热键注册：smoke 下构造成功即视为通过
        _window = new MainWindow();
        _window.Activate();

        if (smoke)
            Exit();
    }
}
