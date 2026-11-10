using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

/// <summary>
/// 同一 exe 的隐藏 helper 模式。父进程先把它加入 Job Object，再放行 Python，
/// 保证 Python 的第一条指令执行前已经受 Job 控制。
/// </summary>
public static class ScriptHelperHost
{
    private const string HelperArgument = "--script-helper";
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan OutputDrainGrace = TimeSpan.FromSeconds(2);

    public static bool TryRun(string[] args)
    {
        if (args.Length < 5
            || !string.Equals(args[1], HelperArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int exitCode = Run(args[2], args[3], args[4]);
        Console.Out.Flush();
        Console.Error.Flush();
        Environment.Exit(exitCode);
        return true;
    }

    private static int Run(string specPath, string readyEventName, string startEventName)
    {
        try
        {
            PythonRunSpec? spec = PythonRunSpec.TryParse(File.ReadAllText(specPath));
            if (spec is null)
            {
                Console.Error.WriteLine("Python 运行规格无效。");
                return 2;
            }

            using EventWaitHandle ready = EventWaitHandle.OpenExisting(readyEventName);
            ready.Set();

            using EventWaitHandle start = EventWaitHandle.OpenExisting(startEventName);
            if (!start.WaitOne(HandshakeTimeout))
            {
                Console.Error.WriteLine("等待父进程启动信号超时。");
                return 2;
            }

            return RunPython(spec);
        }
        catch (Exception ex)
        {
            try
            {
                Console.Error.WriteLine($"启动 Python 失败：{ex.Message}");
            }
            catch
            {
                // helper 没有可用标准错误时只能返回失败码。
            }

            return 1;
        }
        finally
        {
            try
            {
                File.Delete(specPath);
            }
            catch
            {
                // 运行规格清理失败不影响脚本结果。
            }
        }
    }

    private static int RunPython(PythonRunSpec spec)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = spec.PythonPath,
            WorkingDirectory = spec.WorkingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        startInfo.ArgumentList.Add("-u");
        startInfo.ArgumentList.Add(spec.ScriptPath);
        startInfo.Environment["PYTHONUTF8"] = "1";
        startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
        startInfo.Environment["PYTHONUNBUFFERED"] = "1";

        using Process? process = Process.Start(startInfo);
        if (process is null)
        {
            Console.Error.WriteLine("系统未返回 Python 进程。");
            return 1;
        }

        process.StandardInput.Close();
        Stream stdout = Console.OpenStandardOutput();
        Stream stderr = Console.OpenStandardError();
        Task stdoutTask = process.StandardOutput.BaseStream.CopyToAsync(stdout);
        Task stderrTask = process.StandardError.BaseStream.CopyToAsync(stderr);
        process.WaitForExit();

        try
        {
            Task.WhenAll(stdoutTask, stderrTask)
                .WaitAsync(OutputDrainGrace)
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            // 残留后代由父进程关闭 Job 时终止。
        }

        return process.ExitCode;
    }
}
