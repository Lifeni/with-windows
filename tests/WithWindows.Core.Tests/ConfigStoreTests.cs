using System.Text.Json;
using WithWindows.Core.Config;

namespace WithWindows.Core.Tests;

public class ConfigStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ww-cfg-" + Guid.NewGuid().ToString("N"));

    public ConfigStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private ConfigStore NewStore() => new(System.IO.Path.Combine(_dir, "config.json"));

    [Fact]
    public void Load_版本1_迁移为版本5并保留原配置()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 1,
              "tools": [
                { "id": "notepad", "enabled": true, "hotkey": "Ctrl+Alt+N" },
                { "id": "display_mode", "enabled": false, "hotkey": null }
              ],
              "displayMode": { "modes": [ "internal", "external" ] },
              "windowState": { "width": 1024, "height": 700, "x": 0, "y": 0, "notepadFontSize": 18 }
            }
            """);

        AppConfig config = store.Load();

        Assert.Equal(5, config.Version);
        Assert.Equal("Ctrl+Alt+N", config.Tools.Single(t => t.Id == "notepad").Hotkey);
        Assert.Equal(new[] { "internal", "external" }, config.DisplayMode.Modes);
        Assert.Equal(1024d, config.WindowState.Width);
        Assert.Equal(18d, config.WindowState.NotepadFontSize);
        Assert.Equal("", config.Chat.BaseUrl);
        Assert.Equal(ChatSettings.DefaultSystemPrompt, config.Chat.SystemPrompt);
        Assert.Contains("\"version\": 5", File.ReadAllText(store.Path));
    }

    [Fact]
    public void Load_版本4_迁移为版本5并默认打开记事()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 4,
              "windowState": {
                "width": 1024,
                "height": 700
              }
            }
            """);

        AppConfig config = store.Load();

        Assert.Equal(5, config.Version);
        Assert.Equal(1024d, config.WindowState.Width);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(store.Path));
        Assert.Equal(5, saved.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(
            "notepad",
            saved.RootElement.GetProperty("windowState").GetProperty("defaultPage").GetString());
    }

    [Fact]
    public void Load_非法默认页_回退为记事()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 5,
              "windowState": {
                "width": 1024,
                "height": 700,
                "defaultPage": "unknown"
              }
            }
            """);

        AppConfig config = store.Load();

        Assert.Equal(5, config.Version);
        Assert.Equal(1024d, config.WindowState.Width);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(store.Path));
        Assert.Equal(
            "notepad",
            saved.RootElement.GetProperty("windowState").GetProperty("defaultPage").GetString());
    }

    [Theory]
    [InlineData("1")]
    [InlineData("true")]
    [InlineData("{}")]
    public void Load_默认页类型错误_回退为记事且保留其他配置(string invalidDefaultPage)
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            $$"""
            {
              "version": 5,
              "tools": [
                { "id": "notepad", "enabled": true, "hotkey": "F13" }
              ],
              "displayMode": { "modes": [ "internal", "external" ] },
              "windowState": {
                "width": 1024,
                "height": 700,
                "defaultPage": {{invalidDefaultPage}}
              },
              "chat": {
                "baseUrl": "https://example.test/v1",
                "model": "demo"
              }
            }
            """);

        AppConfig config = store.Load();

        Assert.Equal(WindowDefaultPage.Notepad, config.WindowState.DefaultPage);
        Assert.Equal(1024d, config.WindowState.Width);
        Assert.Equal("F13", config.Tools.Single(t => t.Id == "notepad").Hotkey);
        Assert.Equal("demo", config.Chat.Model);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(store.Path));
        Assert.Equal(
            WindowDefaultPage.Notepad,
            saved.RootElement.GetProperty("windowState").GetProperty("defaultPage").GetString());
    }

    [Fact]
    public void Load_窗口状态为null_补默认值且保留其他配置()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 5,
              "tools": [
                { "id": "notepad", "enabled": true, "hotkey": "F13" }
              ],
              "windowState": null,
              "chat": {
                "baseUrl": "https://example.test/v1",
                "model": "demo"
              }
            }
            """);

        Exception? exception = Record.Exception(() => store.Load());

        Assert.Null(exception);
        Assert.Equal("F13", store.Load().Tools.Single(t => t.Id == "notepad").Hotkey);
        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(store.Path));
        JsonElement windowState = saved.RootElement.GetProperty("windowState");
        Assert.Equal(JsonValueKind.Object, windowState.ValueKind);
        Assert.Equal(
            WindowDefaultPage.Notepad,
            windowState.GetProperty("defaultPage").GetString());
    }

    [Fact]
    public void Load_版本5缺失默认页_回写为记事()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 5,
              "windowState": {
                "width": 1024,
                "height": 700
              }
            }
            """);

        _ = store.Load();

        using JsonDocument saved = JsonDocument.Parse(File.ReadAllText(store.Path));
        Assert.True(
            saved.RootElement.GetProperty("windowState").TryGetProperty("defaultPage", out JsonElement defaultPage));
        Assert.Equal(WindowDefaultPage.Notepad, defaultPage.GetString());
    }

    [Fact]
    public void Chat配置_保存后可完整读回()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Chat.BaseUrl = "https://example.test/v1";
        config.Chat.Model = "local-model";
        config.Chat.SystemPrompt = "回答简洁。";

        store.Save(config);
        AppConfig reloaded = store.Load();

        Assert.Equal("https://example.test/v1", reloaded.Chat.BaseUrl);
        Assert.Equal("local-model", reloaded.Chat.Model);
        Assert.Equal("回答简洁。", reloaded.Chat.SystemPrompt);
    }

    [Fact]
    public void Load_版本2空提示词_迁移为默认提示()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 2,
              "chat": {
                "baseUrl": "https://example.test/v1",
                "model": "demo",
                "systemPrompt": ""
              }
            }
            """);

        AppConfig config = store.Load();

        Assert.Equal(5, config.Version);
        Assert.Equal(ChatSettings.DefaultSystemPrompt, config.Chat.SystemPrompt);
    }

    [Fact]
    public void Load_版本4空提示词_保留用户主动清空()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 4,
              "chat": {
                "baseUrl": "https://example.test/v1",
                "model": "demo",
                "systemPrompt": ""
              }
            }
            """);

        AppConfig config = store.Load();

        Assert.Equal(5, config.Version);
        Assert.Equal("", config.Chat.SystemPrompt);
    }

    [Fact]
    public void Load_新格式版本3_迁移为版本5并保留主动清空()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 3,
              "chat": {
                "baseUrl": "https://example.test/v1",
                "model": "demo",
                "systemPrompt": ""
              }
            }
            """);

        AppConfig config = store.Load();

        Assert.Equal(5, config.Version);
        Assert.Equal("https://example.test/v1", config.Chat.BaseUrl);
        Assert.Equal("demo", config.Chat.Model);
        Assert.Equal("", config.Chat.SystemPrompt);
    }

    [Fact]
    public void Load_全新配置_不预置任何热键()
    {
        var store = NewStore();
        store.EnsureExists();
        var config = store.Load();
        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.All(config.Tools, t => Assert.True(string.IsNullOrEmpty(t.Hotkey)));
        Assert.Contains(config.Tools, t => t.Id == "notepad");
        Assert.Contains(config.Tools, t => t.Id == "display_mode");
        Assert.Equal(ChatSettings.DefaultSystemPrompt, config.Chat.SystemPrompt);
    }

    [Fact]
    public void Load_未知工具_被清理并回写()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """
            {
              "version": 1,
              "tools": [
                { "id": "notepad", "enabled": true, "hotkey": null },
                { "id": "ghost", "enabled": true, "hotkey": "F14" }
              ]
            }
            """);
        var config = store.Load();
        Assert.DoesNotContain(config.Tools, t => t.Id == "ghost");
        Assert.DoesNotContain("ghost", File.ReadAllText(store.Path));
    }

    [Fact]
    public void Load_缺失的已知工具_自动补回()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """{ "version": 1, "tools": [] }""");
        var config = store.Load();
        Assert.Contains(config.Tools, t => t.Id == "notepad");
        Assert.Contains(config.Tools, t => t.Id == "display_mode");
    }

    [Fact]
    public void Load_热键为空串_视为不绑定()
    {
        var store = NewStore();
        File.WriteAllText(store.Path,
            """{ "version": 1, "tools": [ { "id": "notepad", "enabled": true, "hotkey": "" } ] }""");
        var tool = store.Load().Tools.Single(t => t.Id == "notepad");
        Assert.True(string.IsNullOrEmpty(tool.Hotkey));
    }

    [Fact]
    public void Load_旧数组格式_重建为默认配置()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """[ { "hotkey": "F13", "action": "notepad" } ]""");
        var config = store.Load();
        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.All(config.Tools, t => Assert.True(string.IsNullOrEmpty(t.Hotkey)));
    }

    [Fact]
    public void Load_版本号不符_重建为默认配置()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """{ "version": 3, "bindings": { "notepad": "F13" } }""");
        var config = store.Load();
        Assert.Equal(AppConfig.CurrentVersion, config.Version);
        Assert.All(config.Tools, t => Assert.True(string.IsNullOrEmpty(t.Hotkey)));
    }

    [Fact]
    public void Load_位置_0_是合法坐标()
    {
        var store = NewStore();
        File.WriteAllText(store.Path, """{ "version": 1, "windowState": { "x": 0, "y": 0 } }""");
        var state = store.Load().WindowState;
        Assert.Equal(0d, state.X);
        Assert.Equal(0d, state.Y);
    }

    [Fact]
    public void Save_保留上一份为备份且不留临时文件()
    {
        var store = NewStore();
        var first = new AppConfig();
        first.Tools.Single(t => t.Id == "notepad").Hotkey = "Ctrl+Alt+N";
        store.Save(first);
        var second = new AppConfig();
        second.Tools.Single(t => t.Id == "notepad").Hotkey = "Ctrl+Alt+M";
        store.Save(second);
        Assert.Equal("Ctrl+Alt+M", store.Load().Tools.Single(t => t.Id == "notepad").Hotkey);
        Assert.True(File.Exists(store.BackupPath));
        Assert.False(File.Exists(store.Path + ".tmp"));
    }

    [Fact]
    public void Load_主文件损坏_从备份恢复()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.Tools.Single(t => t.Id == "notepad").Hotkey = "Ctrl+Alt+N";
        store.Save(config);
        store.Save(config);
        File.WriteAllText(store.Path, "{ \"version\": 1, ");
        Assert.Equal("Ctrl+Alt+N", store.Load().Tools.Single(t => t.Id == "notepad").Hotkey);
    }

    [Fact]
    public void Load_主文件与备份都损坏_抛异常()
    {
        var store = NewStore();
        store.Save(new AppConfig());
        store.Save(new AppConfig());
        File.WriteAllText(store.Path, "{ 坏");
        File.WriteAllText(store.BackupPath, "{ 也坏");
        Assert.Throws<InvalidDataException>(() => store.Load());
    }

    [Fact]
    public void Save_往返一致()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.DisplayMode.Modes = new List<string> { "internal", "external" };
        config.WindowState.Width = 1024;
        store.Save(config);
        var reloaded = store.Load();
        Assert.Equal(new[] { "internal", "external" }, reloaded.DisplayMode.Modes);
        Assert.Equal(1024d, reloaded.WindowState.Width);
    }

    [Fact]
    public void Save_默认打开窗口_往返一致()
    {
        var store = NewStore();
        var config = new AppConfig();
        config.WindowState.DefaultPage = WindowDefaultPage.AiChat;

        store.Save(config);
        AppConfig reloaded = store.Load();

        Assert.Equal(WindowDefaultPage.AiChat, reloaded.WindowState.DefaultPage);
    }
}
