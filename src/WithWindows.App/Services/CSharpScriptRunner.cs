using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WithWindows.Core.Diagnostics;
using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

/// <summary>
/// 启动隐藏 helper，让 helper 使用 Roslyn 编译并执行 C#；负责超时、取消和输出落盘。
/// </summary>
public sealed class CSharpScriptRunner : IDisposable
{
    private const int MaximumBytesPerStream = 2 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly HashSet<Process> _activeHelpers = [];
    private bool _stopping;
    private bool _disposed;

    public async Task<CSharpRunResult> RunAsync(
        CSharpRunRequest request,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (cancellationToken.IsCancellationRequested)
        {
            return new CSharpRunResult(
                ScheduledTaskRunStatus.Cancelled,
                ExitCode: null,
                ErrorMessage: "脚本尚未启动即被停止。",
                StdoutTruncated: false,
                StderrTruncated: false);
        }
        lock (_gate)
        {
            if (_stopping)
            {
                return new CSharpRunResult(
                    ScheduledTaskRunStatus.Cancelled,
                    ExitCode: null,
                    ErrorMessage: "应用正在退出。",
                    StdoutTruncated: false,
                    StderrTruncated: false);
            }
        }

        if (!File.Exists(request.ScriptPath))
        {
            return Failed($"C# 脚本不存在：{request.ScriptPath}");
        }

        if (!Directory.Exists(request.WorkingDirectory))
        {
            return Failed($"工作目录不存在：{request.WorkingDirectory}");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(request.StdoutLogPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(request.StderrLogPath)!);
        Directory.CreateDirectory(AppPaths.ScheduledTaskRunnerDirectory);

        string specPath = Path.Combine(
            AppPaths.ScheduledTaskRunnerDirectory,
            $"{request.RunId}.json");
        string suffix = Guid.NewGuid().ToString("N");
        string readyEventName = $@"Local\WithWindows.ScriptRunner.Ready.{suffix}";
        string startEventName = $@"Local\WithWindows.ScriptRunner.Start.{suffix}";
        var spec = new CSharpRunSpec(
            request.ScriptPath,
            request.WorkingDirectory);
        File.WriteAllText(specPath, spec.Serialize());

        using var ready = new EventWaitHandle(
            false,
            EventResetMode.ManualReset,
            readyEventName);
        using var start = new EventWaitHandle(
            false,
            EventResetMode.ManualReset,
            startEventName);
        Process? helper = null;
        Task<bool>? stdoutTask = null;
        Task<bool>? stderrTask = null;
        bool truncatedStdout = false;
        bool truncatedStderr = false;
        try
        {
            string executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("无法定位当前可执行文件。");
            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.ArgumentList.Add("--script-helper");
            startInfo.ArgumentList.Add(specPath);
            startInfo.ArgumentList.Add(readyEventName);
            startInfo.ArgumentList.Add(startEventName);

            helper = Process.Start(startInfo)
                ?? throw new InvalidOperationException("系统未返回 script helper 进程。");
            lock (_gate)
                _activeHelpers.Add(helper);

            bool helperReady = await Task.Run(
                () => ready.WaitOne(TimeSpan.FromSeconds(20)),
                cancellationToken);
            if (!helperReady)
                throw new InvalidOperationException("script helper 未完成启动握手。");

            cancellationToken.ThrowIfCancellationRequested();
            var outputSequence = new OutputSequenceCounter();
            start.Set();
            stdoutTask = CopyToLogAsync(
                helper.StandardOutput,
                request.StdoutLogPath,
                MaximumBytesPerStream,
                ScheduledTaskOutputStream.StandardOutput,
                outputSequence);
            stderrTask = CopyToLogAsync(
                helper.StandardError,
                request.StderrLogPath,
                MaximumBytesPerStream,
                ScheduledTaskOutputStream.StandardError,
                outputSequence);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));
            ScheduledTaskRunStatus status;
            try
            {
                await helper.WaitForExitAsync(timeout.Token);
                status = helper.ExitCode == 0
                    ? ScheduledTaskRunStatus.Succeeded
                    : ScheduledTaskRunStatus.Failed;
            }
            catch (OperationCanceledException)
            {
                status = cancellationToken.IsCancellationRequested
                    ? ScheduledTaskRunStatus.Cancelled
                    : ScheduledTaskRunStatus.TimedOut;
                Terminate(helper);
            }

            Task<bool> completedStdout = stdoutTask!;
            Task<bool> completedStderr = stderrTask!;
            Task outputDrain = Task.WhenAll(completedStdout, completedStderr);
            string? outputError = null;
            bool outputDrainCompleted = false;
            try
            {
                await outputDrain.WaitAsync(TimeSpan.FromSeconds(30));
                outputDrainCompleted = true;
            }
            catch (Exception ex)
            {
                outputError = $"读取脚本输出失败：{ex.Message}";
                try
                {
                    helper.StandardOutput.Dispose();
                    helper.StandardError.Dispose();
                    await outputDrain.WaitAsync(TimeSpan.FromSeconds(2));
                    outputDrainCompleted = true;
                }
                catch
                {
                    _ = outputDrain.ContinueWith(
                        completed => _ = completed.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
                }

                if (outputDrain.IsCompleted)
                    outputDrainCompleted = true;
                if (!outputDrainCompleted)
                    status = ScheduledTaskRunStatus.Interrupted;
                else if (status != ScheduledTaskRunStatus.TimedOut
                    && status != ScheduledTaskRunStatus.Cancelled)
                {
                    status = ScheduledTaskRunStatus.Failed;
                }
            }

            truncatedStdout = completedStdout.IsCompletedSuccessfully
                && completedStdout.Result;
            truncatedStderr = completedStderr.IsCompletedSuccessfully
                && completedStderr.Result;

            int? exitCode = helper.HasExited ? helper.ExitCode : null;
            string? error = status switch
            {
                ScheduledTaskRunStatus.TimedOut => $"脚本超过 {request.TimeoutSeconds} 秒。",
                ScheduledTaskRunStatus.Cancelled => "脚本已停止。",
                ScheduledTaskRunStatus.Interrupted => "脚本输出流未能正常收尾。",
                ScheduledTaskRunStatus.Failed => $"C# 脚本退出码为 {helper.ExitCode}。",
                _ => null,
            };
            if (outputError is not null)
                error = string.IsNullOrWhiteSpace(error) ? outputError : error + " " + outputError;
            return new CSharpRunResult(
                status,
                exitCode,
                error,
                truncatedStdout,
                truncatedStderr);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            if (helper is not null)
                Terminate(helper);
            return new CSharpRunResult(
                ScheduledTaskRunStatus.Cancelled,
                ExitCode: null,
                ErrorMessage: "脚本已停止。",
                StdoutTruncated: false,
                StderrTruncated: false);
        }
        catch (Exception ex)
        {
            if (helper is not null && !helper.HasExited)
                Terminate(helper);
            if (helper is not null
                && (stdoutTask is not null || stderrTask is not null))
            {
                var pumps = new List<Task>();
                if (stdoutTask is not null)
                    pumps.Add(stdoutTask);
                if (stderrTask is not null)
                    pumps.Add(stderrTask);
                Task allPumps = Task.WhenAll(pumps);
                try
                {
                    helper.StandardOutput.Dispose();
                    helper.StandardError.Dispose();
                    await allPumps.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch
                {
                    _ = allPumps.ContinueWith(
                        completed => _ = completed.Exception,
                        CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted,
                        TaskScheduler.Default);
                    return new CSharpRunResult(
                        ScheduledTaskRunStatus.Interrupted,
                        ExitCode: helper.HasExited ? helper.ExitCode : null,
                        ErrorMessage: $"{ex.Message} 脚本输出流未能正常收尾。",
                        StdoutTruncated: false,
                        StderrTruncated: false);
                }
            }

            return Failed(ex.Message);
        }
        finally
        {
            lock (_gate)
            {
                if (helper is not null)
                    _activeHelpers.Remove(helper);
            }
            helper?.Dispose();
            try
            {
                File.Delete(specPath);
            }
            catch
            {
                // 规格文件清理失败时由下次启动清理目录中的孤儿文件。
            }
        }
    }

    public async Task StopAllAsync()
    {
        Process[] helpers;
        lock (_gate)
        {
            _stopping = true;
            helpers = _activeHelpers.ToArray();
        }

        foreach (Process helper in helpers)
        {
            try
            {
                Terminate(helper);
            }
            catch
            {
                // 主动关闭阶段继续处理其余 helper。
            }
        }

        await Task.CompletedTask;
    }

    private static async Task<bool> CopyToLogAsync(
        StreamReader reader,
        string path,
        int maximumBytes,
        ScheduledTaskOutputStream stream,
        OutputSequenceCounter sequence)
    {
        bool truncated = false;
        var budget = new OutputBudget(maximumBytes);
        await using var file = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            bufferSize: 16 * 1024,
            useAsync: true);
        await using var writer = new StreamWriter(
            file,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

        async Task EmitLineAsync(string line)
        {
            if (budget.IsExhausted)
            {
                truncated = true;
                return;
            }

            const int LogEntryOverheadBytes = 128;
            if (budget.Accept(LogEntryOverheadBytes) < LogEntryOverheadBytes)
            {
                truncated = true;
                return;
            }

            int byteCount = Encoding.UTF8.GetByteCount(line);
            if (byteCount == 0)
            {
                await WriteEntryAsync(writer, stream, "", sequence);
                return;
            }

            int allowed = budget.Accept(byteCount);
            if (allowed == 0)
            {
                truncated = true;
                return;
            }

            string output = allowed == byteCount
                ? line
                : TakeUtf8Prefix(line, allowed);
            if (allowed < byteCount)
                truncated = true;
            await WriteEntryAsync(writer, stream, output, sequence);
        }

        const int MaximumBufferedLineCharacters = 64 * 1024;
        char[] buffer = new char[4096];
        var pendingLine = new StringBuilder();
        bool skipRemainderOfLine = false;
        bool previousWasCarriageReturn = false;
        while (true)
        {
            int count = await reader.ReadAsync(buffer);
            if (count == 0)
                break;

            for (int index = 0; index < count; index++)
            {
                char current = buffer[index];
                if (current is not ('\r' or '\n'))
                {
                    previousWasCarriageReturn = false;
                    if (skipRemainderOfLine)
                        continue;

                    pendingLine.Append(current);
                    if (pendingLine.Length >= MaximumBufferedLineCharacters)
                    {
                        await EmitLineAsync(pendingLine.ToString());
                        pendingLine.Clear();
                        skipRemainderOfLine = true;
                        truncated = true;
                    }
                    continue;
                }

                if (current == '\n' && previousWasCarriageReturn)
                {
                    previousWasCarriageReturn = false;
                    continue;
                }

                previousWasCarriageReturn = current == '\r';
                if (!skipRemainderOfLine)
                    await EmitLineAsync(pendingLine.ToString());
                pendingLine.Clear();
                skipRemainderOfLine = false;
            }
        }

        if (pendingLine.Length > 0 && !skipRemainderOfLine)
            await EmitLineAsync(pendingLine.ToString());
        if (truncated)
            await WriteEntryAsync(writer, stream, "输出已截断", sequence);
        await writer.FlushAsync();
        return truncated;
    }

    private static Task WriteEntryAsync(
        StreamWriter writer,
        ScheduledTaskOutputStream stream,
        string text,
        OutputSequenceCounter sequence)
    {
        var entry = new ScheduledTaskOutputLogEntry(
            DateTimeOffset.Now,
            sequence.Next(),
            stream,
            text);
        return writer.WriteLineAsync(ScheduledTaskOutputLogCodec.Serialize(entry));
    }

    private static string TakeUtf8Prefix(string text, int maximumBytes)
    {
        int characters = 0;
        int bytes = 0;
        while (characters < text.Length)
        {
            int length = char.IsHighSurrogate(text[characters])
                && characters + 1 < text.Length
                && char.IsLowSurrogate(text[characters + 1])
                    ? 2
                    : 1;
            int characterBytes = Encoding.UTF8.GetByteCount(
                text.AsSpan(characters, length));
            if (bytes + characterBytes > maximumBytes)
                break;

            characters += length;
            bytes += characterBytes;
        }

        return text[..characters];
    }

    private sealed class OutputSequenceCounter
    {
        private long _value;

        public long Next()
            => Interlocked.Increment(ref _value);
    }

    private static void Terminate(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill();
        }
        catch
        {
            // 进程已经退出或无法终止时保留最终结果。
        }
    }

    private static CSharpRunResult Failed(string message)
        => new(
            ScheduledTaskRunStatus.Failed,
            ExitCode: null,
            ErrorMessage: message,
            StdoutTruncated: false,
            StderrTruncated: false);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        lock (_gate)
        {
            _stopping = true;
            foreach (Process helper in _activeHelpers.ToArray())
            {
                try
                {
                    Terminate(helper);
                }
                catch
                {
                    // Dispose 必须继续释放其余 helper。
                }
            }
        }
    }
}
