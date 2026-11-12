using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

/// <summary>
/// 同一 exe 的隐藏 helper 模式。父进程完成握手后，helper 使用 Roslyn 编译并执行 C# 脚本。
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
            CSharpRunSpec? spec = CSharpRunSpec.TryParse(
                File.ReadAllText(specPath));
            if (spec is null)
            {
                error.WriteLine("C# 运行规格无效。");
                return 2;
            }

            using EventWaitHandle ready =
                EventWaitHandle.OpenExisting(readyEventName);
            ready.Set();

            using EventWaitHandle start =
                EventWaitHandle.OpenExisting(startEventName);
            if (!start.WaitOne(HandshakeTimeout))
            {
                error.WriteLine("等待父进程启动信号超时。");
                return 2;
            }

            Console.OutputEncoding = Encoding.UTF8;
            Directory.SetCurrentDirectory(spec.WorkingDirectory);
            string code = File.ReadAllText(
                spec.ScriptPath,
                new UTF8Encoding(false));
            ScriptOptions options = CreateScriptOptions(spec.ScriptPath);
            try
            {
                _ = CSharpScript.RunAsync(
                    code,
                    options,
                    cancellationToken: CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
                return 0;
            }
            catch (CompilationErrorException ex)
            {
                error.WriteLine("C# 脚本编译失败：");
                foreach (Diagnostic diagnostic in ex.Diagnostics)
                    error.WriteLine(diagnostic.ToString());
                return 1;
            }
            catch (Exception ex)
            {
                error.WriteLine($"C# 脚本执行失败：{FormatException(ex)}");
                return 1;
            }
        }
        catch (Exception ex)
        {
            try
            {
                error.WriteLine($"启动 C# helper 失败：{FormatException(ex)}");
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

    private static ScriptOptions CreateScriptOptions(string scriptPath)
        => ScriptOptions.Default
            .WithFilePath(scriptPath)
            .WithImports(
                "System",
                "System.Collections.Generic",
                "System.Diagnostics",
                "System.IO",
                "System.Linq",
                "System.Net.Http",
                "System.Text",
                "System.Text.Json",
                "System.Threading",
                "System.Threading.Tasks")
            .WithReferences(
                typeof(object).Assembly,
                typeof(Console).Assembly,
                typeof(Enumerable).Assembly,
                typeof(HttpClient).Assembly,
                typeof(Process).Assembly,
                typeof(JsonSerializer).Assembly);

    private static string FormatException(Exception exception)
    {
        var builder = new StringBuilder();
        Exception? current = exception;
        while (current is not null)
        {
            if (builder.Length > 0)
                builder.Append(" → ");
            builder.Append(current.GetType().Name);
            builder.Append("：");
            builder.Append(current.Message);
            current = current.InnerException;
        }

        return builder.ToString();
    }
}
