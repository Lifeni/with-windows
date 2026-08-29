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
              "bindings": { "notepad": "F13", "theme": "Ctrl+Shift+F14" },
              "displayMode": { "modes": [ "internal", "clone", "extend" ] },
              "theme": { "enabled": true, "latitude": 39.9, "longitude": 116.4, "offsetMinutes": 30 },
              "ai": { "baseUrl": "http://127.0.0.1:11434/v1", "apiKey": "x", "model": "qwen2.5" }
            }
            """);

        var config = NewStore().Load();

        Assert.Equal("F13", config.Bindings["notepad"]);
        Assert.Equal("Ctrl+Shift+F14", config.Bindings["theme"]);
        Assert.Equal(new[] { "internal", "clone", "extend" }, config.DisplayMode.Modes);
        Assert.True(config.Theme.Enabled);
        Assert.Equal(39.9, config.Theme.Latitude);
        Assert.Equal(30, config.Theme.OffsetMinutes);
        Assert.Equal("qwen2.5", config.Ai.Model);
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
        Assert.Equal("F14", config.Bindings["theme"]);
        Assert.Equal("F15", config.Bindings["display_mode"]);
        // display_mode 参数归位
        Assert.Equal(new[] { "internal", "extend" }, config.DisplayMode.Modes);
        // auto_theme 参数归位（字符串数字宽容解析）
        Assert.Equal(39.9, config.Theme.Latitude);
        Assert.Equal(116.4, config.Theme.Longitude);
        Assert.Equal(10, config.Theme.OffsetMinutes);
        // 已回写为 v3 格式（文件根不再是数组）
        var root = System.Text.Json.JsonDocument.Parse(File.ReadAllText(store.Path)).RootElement;
        Assert.Equal(System.Text.Json.JsonValueKind.Object, root.ValueKind);
    }

    [Fact]
    public void Load_InvalidJson_Throws()
    {
        File.WriteAllText(NewStore().Path, "{ not json");

        Assert.ThrowsAny<Exception>(() => NewStore().Load());
    }

    [Fact]
    public void Save_RoundTrips()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Bindings["notepad"] = "Ctrl+Alt+N";
        config.DisplayMode.Modes = new List<string> { "internal", "external" };
        config.Ai.BaseUrl = "http://localhost:8080";

        store.Save(config);
        var reloaded = store.Load();

        Assert.Equal("Ctrl+Alt+N", reloaded.Bindings["notepad"]);
        Assert.Equal(new[] { "internal", "external" }, reloaded.DisplayMode.Modes);
        Assert.Equal("http://localhost:8080", reloaded.Ai.BaseUrl);
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
        Assert.Equal("F14", config.Bindings["theme"]);
        Assert.Equal("F15", config.Bindings["display_mode"]);
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
