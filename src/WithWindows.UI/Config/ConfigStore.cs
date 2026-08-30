using System.Text.Json;
using System.Text.Json.Serialization;

namespace WithWindows.Config;

/// <summary>应用配置（v3）：热键绑定 + 各功能参数。由 ConfigStore 读写 %APPDATA%\WithWindows\config.json。</summary>
public sealed class AppConfig
{
    /// <summary>动作名 → 热键字符串。动作：notepad / theme / display_mode。</summary>
    public Dictionary<string, string> Bindings { get; set; } = new()
    {
        ["notepad"] = "F13",
        ["display_mode"] = "F15",
    };

    public DisplayModeConfig DisplayMode { get; set; } = new();
}

/// <summary>屏幕切换参数。</summary>
public sealed class DisplayModeConfig
{
    /// <summary>toggle 循环的候选模式，默认 internal/extend。</summary>
    public List<string> Modes { get; set; } = new() { "internal", "extend" };
}

/// <summary>配置读写（v3）。首次启动自举默认值；旧 v2 数组格式自动迁移为 v3 对象格式。</summary>
public sealed class ConfigStore
{
    private static readonly string DefaultConfigJson =
        """
        {
          "bindings": {
            "notepad": "F13",
            "display_mode": "F15"
          },
          "displayMode": {
            "modes": [ "internal", "extend" ]
          }
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // 兼容旧配置的字符串数字（如 "offsetMinutes": "0"）
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public string Path { get; }

    public ConfigStore(string path) => Path = path;

    /// <summary>配置文件不存在时创建默认配置。已存在则不动（保留用户修改）。</summary>
    public void EnsureExists(Logger log)
    {
        if (File.Exists(Path)) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, DefaultConfigJson);
        log.Info($"已创建默认配置: {Path}");
    }

    /// <summary>加载配置；旧 v2 数组格式自动迁移并回写为 v3。</summary>
    public AppConfig Load()
    {
        string json = File.ReadAllText(Path);
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"配置 JSON 解析失败：{ex.Message}", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var config = MigrateFromV2(doc.RootElement);
                Save(config);
                return config;
            }
            return doc.RootElement.Deserialize<AppConfig>(JsonOptions) ?? new AppConfig();
        }
    }

    public void Save(AppConfig config)
        => File.WriteAllText(Path, JsonSerializer.Serialize(config, JsonOptions));

    /// <summary>v2 数组格式 → v3：条目热键/动作并入 bindings，display_mode/auto_theme 参数归位。</summary>
    private static AppConfig MigrateFromV2(JsonElement array)
    {
        var config = new AppConfig();
        config.Bindings = new Dictionary<string, string>();

        foreach (var item in array.EnumerateArray())
        {
            string? action = item.TryGetProperty("action", out var a) ? a.GetString() : null;
            if (action is null) continue;

            string? hotkey = item.TryGetProperty("hotkey", out var h) ? h.GetString() : null;
            if (!string.IsNullOrWhiteSpace(hotkey) && !config.Bindings.ContainsKey(action))
                config.Bindings[action] = hotkey;

            if (!item.TryGetProperty("args", out var args)) continue;

            if (action == "display_mode" && args.TryGetProperty("modes", out var modes))
            {
                config.DisplayMode.Modes = modes.EnumerateArray()
                    .Select(m => m.GetString() ?? "")
                    .Where(s => s.Length > 0)
                    .ToList();
            }
        }
        return config;
    }
}
