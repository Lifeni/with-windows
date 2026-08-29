namespace WithWindows.Config;

/// <summary>配置条目：热键 → 动作 + 参数。热键可省略（声明式条目，如 auto_theme，由对应组件消费）。</summary>
public sealed class ConfigEntry
{
    public string? Hotkey { get; init; }
    public required string Action { get; init; }
    public object? Args { get; init; }
}

/// <summary>
/// 配置读写。运行时文件在 exe 旁 config/config.json；首次启动自举写入默认配置。
/// 使用内置 MiniJson 解析（仅对象/数组/字符串），零外部依赖，发布产物为单个 exe。
/// </summary>
public sealed class ConfigStore
{
    private static readonly string DefaultConfigJson =
        """
        [
          { "hotkey": "F13", "action": "notepad" },
          { "hotkey": "F14", "action": "theme", "args": { "mode": "toggle" } },
          { "hotkey": "F15", "action": "display_mode", "args": { "mode": "toggle", "modes": ["internal", "extend"] } }
        ]
        """;

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

    public List<ConfigEntry> Load()
    {
        string json = File.ReadAllText(Path);
        JsonValue root;
        try
        {
            root = JsonValue.Parse(json);
        }
        catch (FormatException ex)
        {
            throw new InvalidDataException($"配置 JSON 解析失败：{ex.Message}", ex);
        }

        if (root is not JsonArray array)
            throw new InvalidDataException("配置内容应为 JSON 数组");

        var entries = new List<ConfigEntry>(array.Items.Count);
        foreach (var item in array.Items)
        {
            if (item is not JsonObject obj)
                throw new InvalidDataException("配置条目应为 JSON 对象");

            var hotkey = obj.TryGet("hotkey", out var hk) ? hk as JsonString : null;
            var action = obj.TryGet("action", out var act) ? act as JsonString : null;
            if (action is null)
                throw new InvalidDataException("配置条目缺少 action 字符串字段（hotkey 可省略）");

            obj.TryGet("args", out var args);
            entries.Add(new ConfigEntry
            {
                Hotkey = hotkey?.Value,
                Action = action.Value,
                Args = args,
            });
        }
        return entries;
    }
}
