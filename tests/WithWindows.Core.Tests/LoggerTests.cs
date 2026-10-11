using WithWindows.Core.Diagnostics;

namespace WithWindows.Core.Tests;

public class LoggerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ww-log-" + Guid.NewGuid().ToString("N"));

    public LoggerTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Open_写入后可读回且带级别()
    {
        using (var logger = Logger.Open(_dir))
        {
            logger.Info("测试信息");
            logger.Error("测试错误");
        }

        string text = File.ReadAllText(Path.Combine(_dir, "log.txt"));
        Assert.Contains("[INFO] 测试信息", text);
        Assert.Contains("[ERROR] 测试错误", text);
    }

    [Fact]
    public void Open_超过上限_轮转为_log1()
    {
        string path = Path.Combine(_dir, "log.txt");
        File.WriteAllText(path, new string('x', 1024 * 1024 + 16));

        using (Logger.Open(_dir)) { }

        Assert.True(File.Exists(Path.Combine(_dir, "log.1.txt")));
    }

    [Fact]
    public void Null_不抛异常()
    {
        Logger.Null.Info("丢弃");
        Logger.Null.Error("丢弃");
    }
}
