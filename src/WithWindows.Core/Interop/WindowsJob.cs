using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.JobObjects;

namespace WithWindows.Core.Interop;

/// <summary>
/// Windows Job Object 薄封装。关闭句柄时自动终止其中的全部进程。
/// </summary>
public sealed class WindowsJob : IDisposable
{
    private readonly SafeFileHandle _handle;
    private bool _disposed;

    private WindowsJob(SafeFileHandle handle) => _handle = handle;

    public static WindowsJob CreateKillOnClose()
    {
        SafeFileHandle handle = PInvoke.CreateJobObject(null, null);
        if (handle.IsInvalid)
            throw new Win32Exception();

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation =
            {
                LimitFlags = JOB_OBJECT_LIMIT.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        unsafe
        {
            if (!PInvoke.SetInformationJobObject(
                    new HANDLE(handle.DangerousGetHandle()),
                    JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation,
                    &info,
                    (uint)sizeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION)))
            {
                int error = Marshal.GetLastWin32Error();
                handle.Dispose();
                throw new Win32Exception(error, "设置 Job Object 限制失败。");
            }
        }

        return new WindowsJob(handle);
    }

    public void Assign(Process process)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(process);

        if (!PInvoke.AssignProcessToJobObject(
                new HANDLE(_handle.DangerousGetHandle()),
                new HANDLE(process.SafeHandle.DangerousGetHandle())))
        {
            int error = Marshal.GetLastWin32Error();
            throw new Win32Exception(error, "无法把进程加入 Job Object。");
        }
    }

    public void Terminate(uint exitCode = 1)
    {
        if (_disposed)
            return;

        if (!PInvoke.TerminateJobObject(
                new HANDLE(_handle.DangerousGetHandle()),
                exitCode))
        {
            int error = Marshal.GetLastWin32Error();
            if (error != 5)
                throw new Win32Exception(error, "终止 Job Object 失败。");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _handle.Dispose();
    }
}
