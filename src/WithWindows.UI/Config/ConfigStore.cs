using System.Text.Json;
using System.Text.Json.Serialization;

namespace WithWindows.Config;

/// <summary>应用配置（v3）：热键绑定 + 各功能参数。由 ConfigStore 读写 %APPDATA%\WithWindows\config.json。</summary>
public sealed class AppConfig
{
    /// <summary>当前配置模型版本。写入配置文件，供未来的结构迁移判断。</summary>
    public const int CurrentVersion = 3;

    /// <summary>配置模型版本（当前 <see cref="CurrentVersion"/>）。</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>当前支持的动作名。迁移旧配置时用于丢弃已移除的动作（如 v2 的 theme）。</summary>
    public static readonly string[] KnownActions = { "notepad", "display_mode" };

    /// <summary>动作名 → 热键字符串。动作：notepad / display_mode。</summary>
    public Dictionary<string, string> Bindings { get; set; } = new()
    {
        ["notepad"] = "F13",
        ["display_mode"] = "F14",
    };

    public DisplayModeConfig DisplayMode { get; set; } = new();
    public WindowStateConfig WindowState { get; set; } = new();
}

/// <summary>屏幕切换参数。</summary>
public sealed class DisplayModeConfig
{
    /// <summary>toggle 循环的候选模式，默认 internal/extend。</summary>
    public List<string> Modes { get; set; } = new() { "internal", "extend" };
}

/// <summary>窗口状态记忆（尺寸/位置/字体），关闭重开恢复。</summary>
public sealed class WindowStateConfig
{
    public double NotepadFontSize { get; set; } = 14;
    public double NotepadWidth { get; set; } = 520;
    public double NotepadHeight { get; set; } = 780;
    /// <summary>窗口位置；null = 尚未保存（首次运行），不参与恢复。屏幕左上角 (0, 0) 是合法位置，不能用 0 当哨兵值。</summary>
    public double? NotepadX { get; set; }
    public double? NotepadY { get; set; }
    public double SettingsWidth { get; set; } = 520;
    public double SettingsHeight { get; set; } = 780;
    public double? SettingsX { get; set; }
    public double? SettingsY { get; set; }
}

/// <summary>配置读写（v3）。首次启动自举默认值；旧 v2 数组格式自动迁移为 v3 对象格式。</summary>
public sealed class ConfigStore
{
    private static readonly string DefaultConfigJson =
        """
        {
          "version": 3,
          "bindings": {
            "notepad": "F13",
            "display_mode": "F14"
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

    /// <summary>备份路径：保存时自动留一份上一次的内容，主文件损坏时可回退。</summary>
    public string BackupPath => Path + ".bak";

    public ConfigStore(string path) => Path = path;

    /// <summary>配置文件不存在时创建默认配置。已存在则不动（保留用户修改）。</summary>
    public void EnsureExists(Logger log)
    {
        if (File.Exists(Path)) return;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        File.WriteAllText(Path, DefaultConfigJson);
        log.Info($"已创建默认配置: {Path}");
    }

    /// <summary>加载配置；旧 v2 数组格式自动迁移并回写为 v3；主文件损坏时回退到上次成功的备份。</summary>
    public AppConfig Load()
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(Path));
        }
        catch (JsonException ex)
        {
            // JSON 损坏（多为写入过程被中断）：先退到备份，退不了再报错
            return RecoverFromBackup()
                ?? throw new InvalidDataException($"配置 JSON 解析失败：{ex.Message}", ex);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind == JsonValueKind.Array)
            {
                var config = MigrateFromV2(doc.RootElement);
                Save(config);
                return config;
            }

            var loaded = doc.RootElement.Deserialize<AppConfig>(JsonOptions) ?? new AppConfig();
            // 自愈：老版本把 theme 等已移除动作留在 bindings 里，它会占住一个全局热键
            // （实际踩到：theme 与 display_mode 同绑 F14，theme 先注册成功，display_mode 永远注册失败），
            // 界面上又看不到、改不掉，这里直接清掉并回写。
            if (PruneUnknownBindings(loaded))
                Save(loaded);
            return loaded;
        }
    }

    /// <summary>删除已不支持的动作绑定；返回是否发生了变化。</summary>
    private static bool PruneUnknownBindings(AppConfig config)
    {
        List<string> unknown = config.Bindings.Keys
            .Where(action => !AppConfig.KnownActions.Contains(action))
            .ToList();
        foreach (string action in unknown)
            config.Bindings.Remove(action);
        return unknown.Count > 0;
    }

    /// <summary>
    /// 原子写入：先写同目录临时文件再整体替换，避免写入中途崩溃/断电留下截断的 JSON。
    /// 替换时把旧内容留到 <see cref="BackupPath"/>。
    /// </summary>
    public void Save(AppConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        string temp = Path + ".tmp";
        File.WriteAllText(temp, json);

        try
        {
            if (File.Exists(Path))
                File.Replace(temp, Path, BackupPath, ignoreMetadataErrors: true);
            else
                File.Move(temp, Path);
        }
        catch (IOException)
        {
            // 少数文件系统不支持 File.Replace：退化为覆盖写（仍保留备份）
            if (File.Exists(Path))
                File.Copy(Path, BackupPath, overwrite: true);
            File.Copy(temp, Path, overwrite: true);
            File.Delete(temp);
        }
    }

    /// <summary>
    /// 从 .bak 恢复：解析成功才写回主文件（直接覆盖，不动 .bak，便于二次损坏时仍有回退点）。
    /// 解析失败返回 null，由调用方按"无法恢复"处理。
    /// </summary>
    private AppConfig? RecoverFromBackup()
    {
        if (!File.Exists(BackupPath)) return null;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(BackupPath));
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            var config = doc.RootElement.Deserialize<AppConfig>(JsonOptions);
            if (config is null) return null;

            File.WriteAllText(Path, JsonSerializer.Serialize(config, JsonOptions));
            return config;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// v2 数组格式 → v3：条目热键/动作并入 bindings，display_mode 参数归位。
    /// 已移除的动作（theme / auto_theme）直接丢弃：留着会占住一个全局热键且界面上看不到、改不掉。
    /// </summary>
    private static AppConfig MigrateFromV2(JsonElement array)
    {
        var config = new AppConfig();
        config.Bindings = new Dictionary<string, string>();

        foreach (var item in array.EnumerateArray())
        {
            string? action = item.TryGetProperty("action", out var a) ? a.GetString() : null;
            if (action is null || !AppConfig.KnownActions.Contains(action)) continue;

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
