using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.Management.Deployment;
using WithWindows.Core.Scheduling;

namespace WithWindows.Services;

/// <summary>
/// 发现并验证本机 Python 3 环境；验证失败的解释器和 Store 占位程序不会进入结果。
/// </summary>
public sealed class PythonEnvironmentDetector
{
    private const string ProbeScript =
        "import json,struct,sys;" +
        "print(json.dumps({" +
        "\"executable\":sys.executable," +
        "\"major\":sys.version_info.major," +
        "\"minor\":sys.version_info.minor," +
        "\"micro\":sys.version_info.micro," +
        "\"bits\":struct.calcsize(\"P\")*8}))";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LauncherTimeout = TimeSpan.FromSeconds(4);

    public async Task<IReadOnlyList<PythonEnvironmentSnapshot>> DetectAsync(
        string? preferredInterpreter = null,
        CancellationToken cancellationToken = default)
    {
        List<PythonCandidate> candidates = await Task.Run(
            () => BuildStaticCandidates(preferredInterpreter),
            cancellationToken).ConfigureAwait(false);
        await AddPyLauncherCandidatesAsync(candidates, cancellationToken)
            .ConfigureAwait(false);

        var results = new List<PythonEnvironmentSnapshot>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (PythonCandidate candidate in candidates
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First()))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(candidate.Path))
                continue;

            PythonEnvironmentSnapshot? snapshot = await ProbeAsync(
                candidate,
                cancellationToken).ConfigureAwait(false);
            if (snapshot is null || !seen.Add(snapshot.Path))
                continue;

            results.Add(snapshot);
        }

        return results
            .OrderByDescending(item => ParseVersion(item.Version))
            .ThenBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<PythonCandidate> BuildStaticCandidates(
        string? preferredInterpreter)
    {
        var candidates = new List<PythonCandidate>();
        if (!string.IsNullOrWhiteSpace(preferredInterpreter))
        {
            bool isStoreAlias =
                PythonEnvironmentParser.IsStoreAliasPath(preferredInterpreter);
            string? packageIdentity = isStoreAlias
                ? FindInstalledStorePythonPackage()
                : null;
            if (!isStoreAlias || packageIdentity is not null)
            {
                candidates.Add(new PythonCandidate(
                    preferredInterpreter,
                    isStoreAlias
                        ? PythonInterpreterSource.StoreAlias
                        : PythonInterpreterSource.Manual,
                    packageIdentity));
            }
        }

        AddPathCandidates(candidates);
        AddCommonInstallCandidates(candidates);
        AddStoreAliasCandidates(candidates);
        return candidates;
    }

    public static bool IsStorePackageInstalled(string? packageIdentity)
    {
        if (string.IsNullOrWhiteSpace(packageIdentity))
            return false;

        try
        {
            return FindInstalledStorePythonPackages()
                .Any(package => string.Equals(
                    package.Id.FamilyName,
                    packageIdentity,
                    StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    private static void AddPathCandidates(List<PythonCandidate> candidates)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            return;

        foreach (string rawDirectory in path.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = rawDirectory.Trim().Trim('"');
            if (directory.Length == 0)
                continue;

            foreach (string executable in new[] { "python.exe", "python3.exe" })
            {
                string candidate = Path.Combine(directory, executable);
                if (PythonEnvironmentParser.IsStoreAliasPath(candidate))
                    continue;

                candidates.Add(new PythonCandidate(
                    candidate,
                    PythonInterpreterSource.Path,
                    PackageIdentity: null));
            }
        }
    }

    private static async Task AddPyLauncherCandidatesAsync(
        List<PythonCandidate> candidates,
        CancellationToken cancellationToken)
    {
        string? launcher = FindExecutable(
            "py.exe",
            [
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs",
                    "Python",
                    "Launcher",
                    "py.exe"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                    "py.exe"),
            ]);
        if (launcher is null)
            return;

        string? output = await RunCommandForTextAsync(
            launcher,
            ["-0p"],
            LauncherTimeout,
            cancellationToken).ConfigureAwait(false);
        if (output is null)
            return;

        foreach (string path in PythonEnvironmentParser.ParsePyLauncherPaths(output))
        {
            candidates.Add(new PythonCandidate(
                path,
                PythonInterpreterSource.PyLauncher,
                PackageIdentity: null));
        }
    }

    private static void AddCommonInstallCandidates(List<PythonCandidate> candidates)
    {
        string[] roots =
        [
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "Python"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        ];

        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!Directory.Exists(root))
                continue;

            try
            {
                foreach (string directory in Directory.EnumerateDirectories(
                    root,
                    "Python*",
                    SearchOption.TopDirectoryOnly))
                {
                    candidates.Add(new PythonCandidate(
                        Path.Combine(directory, "python.exe"),
                        PythonInterpreterSource.Path,
                        PackageIdentity: null));
                }
            }
            catch (Exception ex) when (ex is IOException
                or UnauthorizedAccessException)
            {
                // 某个安装根目录不可读时继续检查其他来源。
            }
        }
    }

    private static void AddStoreAliasCandidates(List<PythonCandidate> candidates)
    {
        string? packageIdentity = FindInstalledStorePythonPackage();
        if (packageIdentity is null)
            return;

        string windowsApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft",
            "WindowsApps");
        foreach (string executable in new[] { "python.exe", "python3.exe" })
        {
            candidates.Add(new PythonCandidate(
                Path.Combine(windowsApps, executable),
                PythonInterpreterSource.StoreAlias,
                packageIdentity));
        }
    }

    private static string? FindInstalledStorePythonPackage()
    {
        try
        {
            return FindInstalledStorePythonPackages()
                .FirstOrDefault()?
                .Id.FamilyName;
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<Package> FindInstalledStorePythonPackages()
    {
        var manager = new PackageManager();
        return manager.FindPackagesForUser(string.Empty)
            .Where(package => package.Id.Name.StartsWith(
                "PythonSoftwareFoundation.Python.",
                StringComparison.OrdinalIgnoreCase))
            .Where(package => package.Status.VerifyIsOK())
            .ToList();
    }

    private static async Task<PythonEnvironmentSnapshot?> ProbeAsync(
        PythonCandidate candidate,
        CancellationToken outerCancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        timeout.CancelAfter(ProbeTimeout);
        Process? process = null;
        Task<string>? stdout = null;
        Task<string>? stderr = null;
        try
        {
            if (PythonEnvironmentParser.IsStoreAliasPath(candidate.Path)
                && (candidate.PackageIdentity is null
                    || !IsStorePackageInstalled(candidate.PackageIdentity)))
            {
                return null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = candidate.Path,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            startInfo.Environment["PYTHONUTF8"] = "1";
            startInfo.Environment["PYTHONIOENCODING"] = "utf-8";
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(ProbeScript);

            process = Process.Start(startInfo);
            if (process is null)
                return null;

            stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            string output = await stdout.ConfigureAwait(false);
            _ = await stderr.ConfigureAwait(false);
            if (process.ExitCode != 0)
                return null;

            PythonEnvironmentSnapshot? snapshot =
                PythonEnvironmentParser.ParseProbeJson(
                    output.Trim(),
                    candidate.Source,
                    candidate.PackageIdentity);
            if (snapshot is null)
                return null;

            return snapshot;
        }
        catch (Exception ex) when (ex is OperationCanceledException
            or InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception)
        {
            return null;
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        _ = process.WaitForExit(1000);
                    }
                }
                catch
                {
                    // 探测进程已经退出或无法终止。
                }

                foreach (Task<string>? pending in new[] { stdout, stderr })
                {
                    if (pending is null)
                        continue;
                    try
                    {
                        _ = pending.GetAwaiter().GetResult();
                    }
                    catch
                    {
                        // 超时取消导致的读取异常无需上报为解释器错误。
                    }
                }

                process.Dispose();
            }
        }
    }

    private static async Task<string?> RunCommandForTextAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            foreach (string argument in arguments)
                startInfo.ArgumentList.Add(argument);

            process = Process.Start(startInfo);
            if (process is null)
                return null;

            Task<string> output = process.StandardOutput.ReadToEndAsync(
                timeoutSource.Token);
            Task<string> error = process.StandardError.ReadToEndAsync(
                timeoutSource.Token);
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            string text = await output.ConfigureAwait(false);
            _ = await error.ConfigureAwait(false);
            return process.ExitCode == 0 ? text : null;
        }
        catch (Exception ex) when (ex is InvalidOperationException
            or IOException
            or System.ComponentModel.Win32Exception
            or OperationCanceledException)
        {
            return null;
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(entireProcessTree: true);
                        _ = process.WaitForExit(1000);
                    }
                }
                catch
                {
                    // 命令已经退出或无法终止。
                }

                process.Dispose();
            }
        }
    }

    private static string? FindExecutable(
        string fileName,
        IReadOnlyList<string> knownPaths)
    {
        foreach (string path in knownPaths)
        {
            if (File.Exists(path))
                return path;
        }

        string? pathVariable = Environment.GetEnvironmentVariable("PATH");
        if (pathVariable is null)
            return null;

        foreach (string rawDirectory in pathVariable.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries))
        {
            string directory = rawDirectory.Trim().Trim('"');
            string candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static Version ParseVersion(string value)
        => Version.TryParse(value, out Version? version)
            ? version
            : new Version(0, 0);

    private sealed record PythonCandidate(
        string Path,
        PythonInterpreterSource Source,
        string? PackageIdentity);
}
