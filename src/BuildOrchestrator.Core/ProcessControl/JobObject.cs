using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// Nested-uyumlu Windows Job Object: KILL_ON_JOB_CLOSE ile son handle kapanışında tüm ağaç ölür.
/// </summary>
public sealed class JobObject : IDisposable
{
    private nint _handle;
    private bool _disposed;

    private JobObject(nint handle) => _handle = handle;

    public static JobObject CreateKillOnClose()
    {
        nint handle = NativeMethods.CreateJobObjectW(nint.Zero, null);
        if (handle == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = NativeMethods.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        if (!NativeMethods.SetInformationJobObject(handle, NativeMethods.JobObjectExtendedLimitInformation, ref info, size))
        {
            int err = Marshal.GetLastWin32Error();
            NativeMethods.CloseHandle(handle);
            throw new Win32Exception(err);
        }
        return new JobObject(handle);
    }

    internal nint Handle => _handle;

    public void Assign(nint processHandle)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NativeMethods.AssignProcessToJobObject(_handle, processHandle))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Terminate(uint exitCode = 1)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!NativeMethods.TerminateJobObject(_handle, exitCode))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public JobCompletionPort AttachCompletionPort()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        nint iocp = NativeMethods.CreateIoCompletionPort(NativeMethods.INVALID_HANDLE_VALUE, nint.Zero, UIntPtr.Zero, 1);
        if (iocp == nint.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());

        var assoc = new NativeMethods.JOBOBJECT_ASSOCIATE_COMPLETION_PORT
        {
            CompletionKey = _handle,
            CompletionPort = iocp,
        };
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_ASSOCIATE_COMPLETION_PORT>();
        if (!NativeMethods.SetInformationJobObject(_handle, NativeMethods.JobObjectAssociateCompletionPortInformation, ref assoc, size))
        {
            int err = Marshal.GetLastWin32Error();
            NativeMethods.CloseHandle(iocp);
            throw new Win32Exception(err);
        }
        return new JobCompletionPort(iocp);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.CloseHandle(_handle); // son handle kapanışı → KILL_ON_JOB_CLOSE kaskadı
        _handle = nint.Zero;
    }
}
