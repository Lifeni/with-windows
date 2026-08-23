namespace WithWindows;

/// <summary>Append-only 日志，写入 exe 旁 data/log.txt。无 UI 常驻程序的可观测性来源。</summary>
public sealed class Logger : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _lock = new();

    public static Logger Open(string dataDir)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            var writer = new StreamWriter(Path.Combine(dataDir, "log.txt"), append: true) { AutoFlush = true };
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
}
