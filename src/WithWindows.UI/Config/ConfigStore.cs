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
    public ThemeAutoConfig Theme { get; set; } = new();
    public AiConfig Ai { get; set; } = new();
}

/// <summary>屏幕切换参数。</summary>
public sealed class DisplayModeConfig
{
    /// <summary>toggle 循环的候选模式，默认 internal/extend。</summary>
    public List<string> Modes { get; set; } = new() { "internal", "extend" };
}

/// <summary>亮暗切换：日出日落自动切换参数（对应旧 auto_theme 条目）。</summary>
public sealed class ThemeAutoConfig
{
    public bool Enabled { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    /// <summary>固定日落/日出时间 "HH:mm"（配置后不再按坐标计算）。</summary>
    public string? Sunrise { get; set; }
    public string? Sunset { get; set; }

    /// <summary>切换点整体偏移（分钟，正数 = 延后）。</summary>
    public int OffsetMinutes { get; set; }
}

/// <summary>AI 助手：OpenAI 兼容端口配置。</summary>
public sealed class AiConfig
{
    public string BaseUrl { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public string Model { get; set; } = "";
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
          },
          "theme": {
            "enabled": false,
            "latitude": 36.6512,
            "longitude": 117.1201,
            "offsetMinutes": 0
          },
          "ai": {
            "baseUrl": "",
            "apiKey": "",
            "model": ""
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
            else if (action == "auto_theme")
            {
                if (args.TryGetProperty("latitude", out var lat)
                    && double.TryParse(lat.GetString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double latitude))
                    config.Theme.Latitude = latitude;
                if (args.TryGetProperty("longitude", out var lon)
                    && double.TryParse(lon.GetString(), System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double longitude))
                    config.Theme.Longitude = longitude;
                if (args.TryGetProperty("offset_minutes", out var off)
                    && int.TryParse(off.GetString(), out int offset))
                    config.Theme.OffsetMinutes = offset;
                if (args.TryGetProperty("sunrise", out var rise))
                    config.Theme.Sunrise = rise.GetString();
                if (args.TryGetProperty("sunset", out var set))
                    config.Theme.Sunset = set.GetString();
            }
        }
        return config;
    }
}
