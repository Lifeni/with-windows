namespace WithWindows;

/// <summary>Append-only 日志，写入 %APPDATA%\WithWindows\log.txt。无 UI 常驻程序的可观测性来源。</summary>
public sealed class Logger : IDisposable
{
    /// <summary>单个日志文件上限；超过后轮转为 log.1.txt（常驻应用日志不能无限增长）。</summary>
    private const long MaxBytes = 1024 * 1024;

    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public static Logger Open(string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            string path = Path.Combine(dataDir, "log.txt");
            Rotate(path);
            var writer = new StreamWriter(path, append: true) { AutoFlush = true };
            return new Logger(writer);
        }
        catch (IOException)
        {
            return Logger.Null; // 日志文件被占用（另一实例竞态等）时降级为丢弃输出，不阻断启动
        }
        catch (UnauthorizedAccessException)
        {
            return Logger.Null;
        }
    }

    /// <summary>丢弃输出的日志，供测试使用。</summary>
    public static Logger Null => new(StreamWriter.Null);

    private Logger(StreamWriter writer) => _writer = writer;

    public void Info(string message) => Write("INFO", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        lock (_lock)
        {
            _writer.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] {message}");
        }
    }

    public void Dispose() => _writer.Dispose();

    /// <summary>日志超过上限时改名保留为 log.1.txt（只留上一份，避免无限占盘）。</summary>
    private static void Rotate(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length <= MaxBytes) return;

            File.Move(path, Path.Combine(info.DirectoryName!, "log.1.txt"), overwrite: true);
        }
        catch (IOException)
        {
            // 轮转是尽力而为：文件被占用时继续往原文件追加，不阻断日志
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
