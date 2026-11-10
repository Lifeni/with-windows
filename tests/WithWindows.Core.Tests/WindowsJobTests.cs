using System.Diagnostics;
using WithWindows.Core.Interop;

namespace WithWindows.Core.Tests;

public sealed class WindowsJobTests
{
    [Fact]
    public async Task Terminate_结束加入Job的子进程()
    {
        using WindowsJob job = WindowsJob.CreateKillOnClose();
        using Process child = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c ping 127.0.0.1 -n 30 > nul",
            UseShellExecute = false,
            CreateNoWindow = true,
        })!;

        job.Assign(child);
        job.Terminate();

        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(child.HasExited);
    }

    [Fact]
    public async Task Terminate_结束加入Job的孙进程()
    {
        string directory = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "ww-job-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string scriptPath = System.IO.Path.Combine(directory, "tree.ps1");
        string pidPath = System.IO.Path.Combine(directory, "child.pid");
        File.WriteAllText(
            scriptPath,
            $$"""
            $child = Start-Process -FilePath "$env:SystemRoot\System32\ping.exe" `
                -ArgumentList "127.0.0.1","-n","30" -PassThru -WindowStyle Hidden
            [IO.File]::WriteAllText("{{pidPath.Replace("\\", "\\\\")}}", "$($child.Id)")
            $child.WaitForExit()
            """);

        try
        {
            using WindowsJob job = WindowsJob.CreateKillOnClose();
            using Process root = Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                ArgumentList =
                {
                    "-NoLogo",
                    "-NoProfile",
                    "-NonInteractive",
                    "-ExecutionPolicy",
                    "Bypass",
                    "-File",
                    scriptPath,
                },
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            job.Assign(root);

            int childId = await WaitForPidAsync(pidPath);
            job.Terminate();
            await root.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Throws<ArgumentException>(() => Process.GetProcessById(childId));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<int> WaitForPidAsync(string path)
    {
        for (int attempt = 0; attempt < 50; attempt++)
        {
            if (File.Exists(path)
                && int.TryParse(File.ReadAllText(path), out int pid))
            {
                return pid;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("测试子进程未写出 PID。");
    }
}
