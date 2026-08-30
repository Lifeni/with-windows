using System.Diagnostics;

namespace WithWindows.Core;

/// <summary>
/// 带宽测速（参考中科大测速实现：下载固定大小文件计时 + 上传固定大小数据计时）。
/// 直连（禁系统代理）以测真实链路带宽。
/// </summary>
public sealed class SpeedTest
{
    private const string BaseUrl = "https://speed.cloudflare.com"; // 中科大测速已下线，改用 Cloudflare 官方测速

    private readonly HttpClient _http;

    public SpeedTest()
    {
        var handler = new HttpClientHandler { UseProxy = false }; // 直连测真实带宽
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
    }

    /// <summary>下载测速：流式读取指定时长，返回 Mbps。</summary>
    public async Task<double> DownloadAsync(int durationSec = 5, CancellationToken ct = default)
    {
        string url = $"{BaseUrl}/__down?bytes=67108864"; // 64MB 测试文件
        var sw = Stopwatch.StartNew();
        long total = 0;
        using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        var buffer = new byte[131072];
        while (sw.Elapsed.TotalSeconds < durationSec)
        {
            int read = await stream.ReadAsync(buffer, ct);
            if (read == 0) break;
            total += read;
        }
        return total * 8.0 / Math.Max(sw.Elapsed.TotalSeconds, 0.1) / 1_000_000.0;
    }

    /// <summary>上传测速：POST 固定大小数据，返回 Mbps。</summary>
    public async Task<double> UploadAsync(int sizeMb = 8, CancellationToken ct = default)
    {
        string url = $"{BaseUrl}/__up";
        var data = new byte[sizeMb * 1024 * 1024];
        var sw = Stopwatch.StartNew();
        using var content = new ByteArrayContent(data);
        using var response = await _http.PostAsync(url, content, ct);
        response.EnsureSuccessStatusCode();
        return data.Length * 8.0 / Math.Max(sw.Elapsed.TotalSeconds, 0.1) / 1_000_000.0;
    }
}
