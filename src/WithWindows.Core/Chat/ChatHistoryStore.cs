using System.Text.Json;

namespace WithWindows.Core.Chat;

/// <summary>当前会话的 JSON 持久化；采用原子替换并保留一份备份。</summary>
public sealed class ChatHistoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public string Path { get; }
    public string BackupPath => Path + ".bak";

    public ChatHistoryStore(string path) => Path = path;

    public ChatSession Load()
    {
        if (!File.Exists(Path))
            return new ChatSession();

        if (TryLoad(Path, out ChatSession? session))
            return session!;

        if (File.Exists(BackupPath) && TryLoad(BackupPath, out session))
        {
            ChatSession recovered = session!;
            Save(recovered);
            return recovered;
        }

        throw new InvalidDataException("AI 会话 JSON 解析失败，且没有可用备份。");
    }

    public void Save(ChatSession session)
    {
        session.Version = ChatSession.CurrentVersion;
        if (session.UpdatedAt == default)
            session.UpdatedAt = DateTimeOffset.Now;

        string json = JsonSerializer.Serialize(session, JsonOptions);
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
            if (File.Exists(Path))
                File.Copy(Path, BackupPath, overwrite: true);
            File.Copy(temp, Path, overwrite: true);
            File.Delete(temp);
        }
    }

    public void Clear()
    {
        DeleteIfExists(Path);
        DeleteIfExists(BackupPath);
        DeleteIfExists(Path + ".tmp");
    }

    private static bool TryLoad(string path, out ChatSession? session)
    {
        session = null;
        try
        {
            session = JsonSerializer.Deserialize<ChatSession>(
                File.ReadAllText(path),
                JsonOptions);
            if (session is null)
                return false;

            session.Version = ChatSession.CurrentVersion;
            session.Messages ??= [];
            if (session.UpdatedAt == default)
                session.UpdatedAt = DateTimeOffset.Now;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
