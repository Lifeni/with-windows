using WithWindows.Config;

namespace WithWindows.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "qa-tests-" + Guid.NewGuid().ToString("N"));

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConfigStore NewStore() => new(Path.Combine(_dir, "config.json"));

    [Fact]
    public void Load_ValidConfig_ReturnsBindingsAndSections()
    {
        File.WriteAllText(NewStore().Path,
            """
            {
              "version": 3,
              "bindings": { "notepad": "F13", "display_mode": "Ctrl+Shift+F14" },
              "displayMode": { "modes": [ "internal", "clone", "extend" ] },
              "windowState": {
                "notepadFontSize": 16, "notepadWidth": 600, "notepadHeight": 800,
                "notepadX": 120, "notepadY": 80
              }
            }
            """);

        var config = NewStore().Load();

        Assert.Equal("F13", config.Bindings["notepad"]);
        Assert.Equal("Ctrl+Shift+F14", config.Bindings["display_mode"]);
        Assert.Equal(new[] { "internal", "clone", "extend" }, config.DisplayMode.Modes);
        Assert.Equal(16d, config.WindowState.NotepadFontSize);
        Assert.Equal(120d, config.WindowState.NotepadX!.Value);
    }

    [Fact]
    public void Load_UnknownSections_AreIgnored()
    {
        // 已移除功能的段落（旧 theme / ai）不该让加载失败，静默忽略即可
        File.WriteAllText(NewStore().Path,
            """
            {
              "bindings": { "notepad": "F13" },
              "theme": { "enabled": true, "latitude": 39.9, "longitude": 116.4 },
              "ai": { "baseUrl": "http://127.0.0.1:11434/v1", "apiKey": "x", "model": "qwen2.5" }
            }
            """);

        var config = NewStore().Load();

        Assert.Equal("F13", config.Bindings["notepad"]);
        Assert.Equal(AppConfig.CurrentVersion, config.Version);
    }

    [Fact]
    public void Load_WindowStateWithoutPosition_LeavesPositionUnset()
    {
        // 首次运行没有保存过位置：(0, 0) 是合法坐标，未保存必须是 null
        File.WriteAllText(NewStore().Path, """{ "bindings": { "notepad": "F13" } }""");

        var state = NewStore().Load().WindowState;

        Assert.Null(state.NotepadX);
        Assert.Null(state.NotepadY);
        Assert.Null(state.SettingsX);
        Assert.Null(state.SettingsY);
    }

    [Fact]
    public void Load_EmptyJson_FallsBackToDefaults()
    {
        File.WriteAllText(NewStore().Path, "{}");

        var config = NewStore().Load();

        Assert.Equal("F13", config.Bindings["notepad"]);
        Assert.Equal(new[] { "internal", "extend" }, config.DisplayMode.Modes);
    }

    [Fact]
    public void Load_V2ArrayFormat_MigratesAndRewrites()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            [
              { "hotkey": "F13", "action": "notepad" },
              { "hotkey": "F14", "action": "theme" },
              { "hotkey": "F15", "action": "display_mode", "args": { "mode": "toggle", "modes": ["internal", "extend"] } },
              { "action": "auto_theme", "args": { "latitude": "39.9", "longitude": "116.4", "offset_minutes": "10" } }
            ]
            """);

        var config = store.Load();

        // 迁移后的 bindings
        Assert.Equal("F13", config.Bindings["notepad"]);
        // 已移除的动作（theme）不保留：否则会占住全局热键且界面看不到、改不掉
        Assert.False(config.Bindings.ContainsKey("theme"));
        Assert.Equal("F15", config.Bindings["display_mode"]); // 迁移保留旧热键
        // display_mode 参数归位
        Assert.Equal(new[] { "internal", "extend" }, config.DisplayMode.Modes);
        // 已回写为 v3 格式（文件根不再是数组）
        var root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.Path)).RootElement;
        Assert.Equal(System.Text.Json.JsonValueKind.Object, root.ValueKind);
    }

    [Fact]
    public void Load_InvalidJsonWithoutBackup_Throws()
    {
        File.WriteAllText(NewStore().Path, "{ not json");

        Assert.Throws<InvalidDataException>(() => NewStore().Load());
    }

    [Fact]
    public void Load_BindingsWithRemovedAction_DropsItAndRewrites()
    {
        // 实际踩到的场景：theme 与 display_mode 都绑 F14，theme 先注册占住 F14，display_mode 永远注册失败
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "bindings": { "notepad": "F13", "theme": "F14", "display_mode": "F14" }
            }
            """);

        var config = store.Load();

        Assert.False(config.Bindings.ContainsKey("theme"));
        Assert.Equal("F14", config.Bindings["display_mode"]);
        // 清理结果已回写：下次启动不必再清理
        Assert.False(store.Load().Bindings.ContainsKey("theme"));
    }

    [Fact]
    public void Save_KeepsPreviousContentAsBackup()
    {
        var store = NewStore();
        var first = new AppConfig();
        first.Bindings["notepad"] = "F13";
        store.Save(first);

        var second = new AppConfig();
        second.Bindings["notepad"] = "Ctrl+Alt+N";
        store.Save(second);

        // 主文件是新内容，.bak 留上一次的内容
        Assert.Equal("Ctrl+Alt+N", store.Load().Bindings["notepad"]);
        Assert.True(File.Exists(store.BackupPath));
        var backup = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.BackupPath)).RootElement;
        Assert.Equal("F13", backup.GetProperty("bindings").GetProperty("notepad").GetString());
    }

    [Fact]
    public void Save_LeavesNoTempFile()
    {
        var store = NewStore();
        store.Save(new AppConfig());
        store.Save(new AppConfig());

        Assert.False(File.Exists(store.Path + ".tmp"));
        Assert.True(File.Exists(store.Path));
    }

    [Fact]
    public void Load_TruncatedPrimary_RecoversFromBackup()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Bindings["notepad"] = "Ctrl+Alt+N";
        store.Save(config);
        store.Save(config); // 第二次保存才产生备份

        File.WriteAllText(store.Path, "{ \"bindings\": { \"notepad\": "); // 模拟写入被中断

        var loaded = store.Load();

        Assert.Equal("Ctrl+Alt+N", loaded.Bindings["notepad"]);
        // 主文件已用备份内容修复，下一次启动仍可正常加载
        Assert.Equal("Ctrl+Alt+N", store.Load().Bindings["notepad"]);
    }

    [Fact]
    public void Load_TruncatedPrimaryAndBackup_Throws()
    {
        var store = NewStore();
        store.Save(new AppConfig());
        store.Save(new AppConfig());

        File.WriteAllText(store.Path, "{ 坏");
        File.WriteAllText(store.BackupPath, "{ 也坏");

        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void Save_RoundTrips()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Bindings["notepad"] = "Ctrl+Alt+N";
        config.DisplayMode.Modes = new List<string> { "internal", "external" };

        store.Save(config);
        var reloaded = store.Load();

        Assert.Equal("Ctrl+Alt+N", reloaded.Bindings["notepad"]);
        Assert.Equal(new[] { "internal", "external" }, reloaded.DisplayMode.Modes);
    }

    [Fact]
    public void EnsureExists_CreatesDefaultConfig()
    {
        var store = NewStore();
        var log = TestLog.Null;

        store.EnsureExists(log);

        Assert.True(File.Exists(store.Path));
        var config = store.Load();
        Assert.Equal("F13", config.Bindings["notepad"]);
        Assert.Equal("F14", config.Bindings["display_mode"]);
    }

    [Fact]
    public void EnsureExists_KeepsExistingFile()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """{ "bindings": { "notepad": "F1" } }""");
        var log = TestLog.Null;

        store.EnsureExists(log);

        Assert.Equal("F1", store.Load().Bindings["notepad"]);
    }
}

/// <summary>测试用空实现，避免依赖真实文件系统日志。</summary>
internal static class TestLog
{
    public static Logger Null => Logger.Null;
}
