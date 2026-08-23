using System.Drawing;
using System.Windows.Forms;
using WithWindows.Actions;
using WithWindows.Config;
using WithWindows.Core;
using WithWindows.Notepad;
namespace WithWindows;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        bool smoke = args.Contains("--smoke", StringComparer.OrdinalIgnoreCase);

        // 单实例守卫：常驻模式下第二个实例直接退出（防日志互斥崩溃与热键冲突）
        using var singleInstance = new SingleInstance();
        if (!smoke && !singleInstance.Owned)
            return 0;

        // .NET Framework 4.8.1：手写初始化（PerMonitorV2 由 app.manifest 声明）
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 数据目录：%APPDATA%\WithWindows（配置 + 日志），exe 目录保持干净
        string dataRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WithWindows");
        using var log = Logger.Open(dataRoot);
        log.Info("WithWindows 启动");

        // 1. 确保配置存在（首次运行自举默认配置）
        var configStore = new ConfigStore(Path.Combine(dataRoot, "config.json"));
        configStore.EnsureExists(log);

        // 2. 加载配置；失败时弹框提示，必须人工修复
        List<ConfigEntry> entries;
        try
        {
            entries = configStore.Load();
        }
        catch (Exception ex)
        {
            log.Error($"配置加载失败: {ex}");
            if (!smoke)
                MessageBox.Show($"配置文件加载失败：\n{ex.Message}\n\n请检查 {configStore.Path}",
                    "With Windows", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }

        // 3. 注册内置动作
        var registry = new ActionRegistry();
        registry.Register(new DisplayModeAction());
        registry.Register(new ThemeAction());
        var notepad = new NotepadHost(dataRoot);
        registry.Register(new NotepadAction(notepad));

        // 3.5 自动亮暗切换：设置来自 auto_theme 条目（声明式、无热键）
        var autoTheme = new AutoThemeScheduler(
            registry.Find("theme")!,
            AutoThemeSettings.FromArgs(entries.FirstOrDefault(e => e.Action == "auto_theme")?.Args),
            log);

        // 4. 注册热键，启动宿主
        string configPath = Path.Combine(dataRoot, "config.json");
        using var app = new App(registry, log, autoTheme, configPath, singleInstance, notepad);
        var failures = app.RegisterAll(entries);
        foreach (var failure in failures)
            log.Error($"注册失败: {failure}");

        if (smoke)
        {
            string? current = DisplayTopology.GetCurrentMode();
            log.Info($"smoke: 配置条目 {entries.Count},注册失败 {failures.Count},当前拓扑 {current ?? "未知"}");
            return failures.Count == 0 ? 0 : 1;
        }

        // 5. 恢复上次启用的自动亮暗切换（smoke 模式不执行；设置缺失等错误只记日志不弹框）
        if (AutoThemeScheduler.GetEnabledFlag())
        {
            string? error = autoTheme.TryStart();
            if (error is not null)
                log.Error($"自动亮暗切换启动失败: {error}");
        }

        if (failures.Count > 0)
        {
            MessageBox.Show(string.Join(Environment.NewLine, failures),
                "WithWindows：部分热键注册失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        Application.Run();
        log.Info("退出");
        return 0;
    }
}
