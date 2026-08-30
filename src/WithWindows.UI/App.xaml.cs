using Microsoft.UI.Xaml;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Interop;

namespace WithWindows;

public partial class App : Application
{
    private Window? _window;
    private Logger? _log; // 生命周期 = 应用：热键回调跨 OnLaunched 使用，不能用 using 局部释放

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

        // 数据目录：%APPDATA%\WithWindows（配置 + 日志），exe 目录保持干净
        string dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WithWindows");
        var log = Logger.Open(dataRoot);
        _log = log;
        log.Info("WithWindows 启动");

        // 1. 确保配置存在（首次运行自举默认配置）
        var configStore = new ConfigStore(Path.Combine(dataRoot, "config.json"));
        configStore.EnsureExists(log);

        // 2. 加载配置（旧 v2 数组格式自动迁移）；失败时弹框提示，必须人工修复
        AppConfig config;
        try
        {
            config = configStore.Load();
        }
        catch (Exception ex)
        {
            log.Error($"配置加载失败: {ex}");
            if (!smoke)
                NativeMethods.MessageBoxW(IntPtr.Zero,
                    $"配置文件加载失败：\n{ex.Message}\n\n请检查 {configStore.Path}",
                    "With Windows", 0x10 /* MB_ICONERROR */);
            return;
        }

        // 3. 主窗口：托盘 + 热键注册 + 动作分发（含自动亮暗恢复）；smoke 不创建托盘，避免快速退出竞态
        _window = new MainWindow(config, log, configStore, withTray: !smoke);

        if (smoke)
        {
            var window = (MainWindow)_window;
            log.Info($"smoke: 绑定 {config.Bindings.Count}，注册失败 {window.RegisterFailures}");
            Exit();
        }
        // 常驻：不显示主窗口（仅托盘），由托盘/热键唤出
    }
}
