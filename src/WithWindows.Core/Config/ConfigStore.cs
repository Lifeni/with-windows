using System.Text.Json;

namespace WithWindows.Core.Config;

/// <summary>
/// 配置读写（v5）。原子替换（临时文件 + File.Replace）+ 旧内容留 .bak + 主文件损坏回退 + 自愈式清理未知工具。
/// 版本 1 至 4 迁移到版本 5；旧 v3 bindings 对象、旧数组与不支持版本重建为默认配置并回写。
/// </summary>
public sealed class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Path { get; }

    /// <summary>备份路径：保存时自动留一份上一次的内容，主文件损坏时可回退。</summary>
    public string BackupPath => Path + ".bak";

    public ConfigStore(string path) => Path = path;

    /// <summary>配置文件不存在时创建默认配置；已存在则不动。返回是否新建。</summary>
    public bool EnsureExists(Action<string>? log = null)
    {
        if (File.Exists(Path)) return false;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        Save(new AppConfig());
        log?.Invoke($"已创建默认配置：{Path}");
        return true;
    }

    /// <summary>加载配置；JSON 损坏时回退备份；旧格式或不支持版本重建默认值。</summary>
    public AppConfig Load()
    {
        string text;
        try
        {
            text = File.ReadAllText(Path);
        }
        catch (FileNotFoundException)
        {
            return new AppConfig();
        }
        catch (DirectoryNotFoundException)
        {
            return new AppConfig();
        }

        AppConfig? config = TryDeserialize(text, out bool unsupportedFormat, out bool needsSave);
        if (config is not null)
        {
            bool normalized = Normalize(config);
            if (needsSave || normalized)
                Save(config);
            return config;
        }

        // 旧格式 / 版本不符：全新 schema，直接重建默认值并回写
        if (unsupportedFormat)
        {
            var defaults = new AppConfig();
            Save(defaults);
            return defaults;
        }

        // JSON 损坏：先退备份，退不了再报错
        AppConfig? recovered = RecoverFromBackup();
        if (recovered is not null) return recovered;
        throw new InvalidDataException("配置 JSON 解析失败，且没有可用的备份");
    }

    /// <summary>
    /// 解析 v1 至 v5 配置。<paramref name="unsupportedFormat"/> 为 true 表示 JSON 合法但不是支持版本
    /// （旧数组格式、缺版本号、版本号不符）；<paramref name="needsSave"/> 表示需要回写。
    /// </summary>
    private static AppConfig? TryDeserialize(
        string text,
        out bool unsupportedFormat,
        out bool needsSave)
    {
        unsupportedFormat = false;
        needsSave = false;

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text);
        }
        catch (JsonException)
        {
            return null;
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object
                || !doc.RootElement.TryGetProperty("version", out JsonElement version)
                || version.ValueKind != JsonValueKind.Number)
            {
                unsupportedFormat = true;
                return null;
            }

            try
            {
                int versionNumber = version.GetInt32();
                if (versionNumber is not (1 or 2 or 3 or 4 or 5))
                {
                    unsupportedFormat = true;
                    return null;
                }

                if (versionNumber == 3
                    && doc.RootElement.TryGetProperty("bindings", out _))
                {
                    unsupportedFormat = true;
                    return null;
                }

                AppConfig config =
                    doc.RootElement.Deserialize<AppConfig>(JsonOptions) ?? new AppConfig();
                bool repaired = false;
                if (config.Tools is null)
                {
                    config.Tools = AppConfig.CreateDefaultTools();
                    repaired = true;
                }

                if (config.DisplayMode is null)
                {
                    config.DisplayMode = new DisplayModeConfig();
                    repaired = true;
                }

                if (config.WindowState is null)
                {
                    config.WindowState = new WindowStateConfig();
                    repaired = true;
                }

                if (config.Chat is null)
                {
                    config.Chat = new ChatSettings();
                    repaired = true;
                }

                if (versionNumber < 3
                    && string.IsNullOrWhiteSpace(config.Chat.SystemPrompt))
                {
                    config.Chat.SystemPrompt = ChatSettings.DefaultSystemPrompt;
                }

                needsSave = versionNumber < AppConfig.CurrentVersion
                    || repaired
                    || !HasValidDefaultPage(doc.RootElement);
                config.Version = AppConfig.CurrentVersion;
                return config;
            }
            catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException)
            {
                unsupportedFormat = true;
                return null;
            }
        }
    }

    private static bool HasValidDefaultPage(JsonElement root)
    {
        if (!root.TryGetProperty("windowState", out JsonElement windowState)
            || windowState.ValueKind != JsonValueKind.Object
            || !windowState.TryGetProperty("defaultPage", out JsonElement defaultPage)
            || defaultPage.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        string? value = defaultPage.GetString();
        return value is WindowDefaultPage.Notepad or WindowDefaultPage.AiChat;
    }

    /// <summary>自愈：丢弃未知工具、去重、补回缺失的已知工具。返回是否发生了变化。</summary>
    private static bool Normalize(AppConfig config)
    {
        bool changed = false;

        string defaultPage = WindowDefaultPage.Normalize(config.WindowState.DefaultPage);
        if (!string.Equals(config.WindowState.DefaultPage, defaultPage, StringComparison.Ordinal))
        {
            config.WindowState.DefaultPage = defaultPage;
            changed = true;
        }

        int removed = config.Tools.RemoveAll(t => !AppConfig.KnownToolIds.Contains(t.Id));
        changed |= removed > 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int duplicates = config.Tools.RemoveAll(t => !seen.Add(t.Id));
        changed |= duplicates > 0;

        foreach (string id in AppConfig.KnownToolIds)
        {
            if (config.Tools.All(t => t.Id != id))
            {
                config.Tools.Add(new ToolBinding { Id = id });
                changed = true;
            }
        }

        return changed;
    }

    /// <summary>原子写入：先写同目录临时文件再整体替换，旧内容留到 <see cref="BackupPath"/>。</summary>
    public void Save(AppConfig config)
    {
        string json = JsonSerializer.Serialize(config, JsonOptions);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
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

    /// <summary>从 .bak 恢复；解析成功才写回主文件。失败返回 null。</summary>
    private AppConfig? RecoverFromBackup()
    {
        if (!File.Exists(BackupPath)) return null;

        try
        {
            AppConfig? config =
                TryDeserialize(File.ReadAllText(BackupPath), out _, out _);
            if (config is null) return null;

            Normalize(config);
            File.WriteAllText(Path, JsonSerializer.Serialize(config, JsonOptions));
            return config;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
