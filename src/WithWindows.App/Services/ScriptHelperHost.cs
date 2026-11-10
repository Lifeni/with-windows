using System;
using System.IO;
using System.Text;
using System.Threading;
using KeraLua;
using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

/// <summary>
/// 同一 exe 的隐藏 helper 模式。父进程先把它加入 Job Object，再放行 Lua，
/// 保证 Lua、它启动的 Python/Node.js 以及其他子进程都受 Job 控制。
/// </summary>
public static class ScriptHelperHost
{
    private const string HelperArgument = "--script-helper";
    private static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(20);

    public static bool TryRun(string[] args)
    {
        if (args.Length < 5
            || !string.Equals(args[1], HelperArgument, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        int exitCode = Run(args[2], args[3], args[4]);
        Environment.Exit(exitCode);
        return true;
    }

    private static int Run(
        string specPath,
        string readyEventName,
        string startEventName)
    {
        using StreamWriter error = new(
            Console.OpenStandardError(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        try
        {
            LuaRunSpec? spec = LuaRunSpec.TryParse(File.ReadAllText(specPath));
            if (spec is null)
            {
                error.WriteLine("Lua 运行规格无效。");
                return 2;
            }

            using EventWaitHandle ready = EventWaitHandle.OpenExisting(readyEventName);
            ready.Set();

            using EventWaitHandle start = EventWaitHandle.OpenExisting(startEventName);
            if (!start.WaitOne(HandshakeTimeout))
            {
                error.WriteLine("等待父进程启动信号超时。");
                return 2;
            }

            Directory.SetCurrentDirectory(spec.WorkingDirectory);
            using var lua = new Lua();
            lua.Encoding = Encoding.UTF8;
            if (!lua.DoFile(spec.ScriptPath))
                return 0;

            string message = lua.ToString(-1);
            error.WriteLine(string.IsNullOrWhiteSpace(message)
                ? "Lua 脚本执行失败。"
                : message);
            return 1;
        }
        catch (Exception ex)
        {
            try
            {
                error.WriteLine($"启动 Lua 失败：{ex.Message}");
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
}
