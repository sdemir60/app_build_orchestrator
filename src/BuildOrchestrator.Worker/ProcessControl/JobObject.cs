using System.Runtime.InteropServices;

namespace BuildOrchestrator.Worker.ProcessControl;

/// <summary>
/// Wraps a Windows Job Object configured with <c>JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE</c> (Section 6.1).
///
/// Every process the Worker launches (MSBuild nodes, VBCSCompiler, pre/post-build events) is assigned
/// to this job. When the job handle closes — for any reason: normal exit, crash, or Task Manager kill —
/// Windows terminates the entire process tree automatically, making orphaned compiler processes
/// impossible. On non-Windows platforms this type is a safe no-op so the Worker still builds/runs.
/// </summary>
public sealed class JobObject : IDisposable
{
    private readonly nint _handle;
    private bool _disposed;

    public bool IsSupported { get; }

    public JobObject()
    {
        if (!OperatingSystem.IsWindows())
        {
            IsSupported = false;
            _handle = nint.Zero;
            return;
        }

        _handle = CreateJobObject(nint.Zero, null);
        if (_handle == nint.Zero)
        {
            throw new InvalidOperationException(
                $"CreateJobObject failed (Win32 error {Marshal.GetLastWin32Error()}).");
        }

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
            }
        };

        var length = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var ptr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr, (uint)length))
            {
                throw new InvalidOperationException(
                    $"SetInformationJobObject failed (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        IsSupported = true;
    }

    /// <summary>Assigns a process (by handle) to this job. No-op when unsupported.</summary>
    public bool AssignProcess(nint processHandle)
    {
        if (!IsSupported || _handle == nint.Zero || processHandle == nint.Zero)
        {
            return false;
        }
        return AssignProcessToJobObject(_handle, processHandle);
    }

    /// <summary>Assigns a process by id to this job.</summary>
    public bool AssignProcess(System.Diagnostics.Process process)
    {
        try
        {
            return AssignProcess(process.Handle);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Forcibly terminates the whole job (all assigned processes and their children).
    /// Used by the timeout escalation path of Stop (Section 6.1 rule 2).
    /// </summary>
    public void Terminate(uint exitCode = 1)
    {
        if (IsSupported && _handle != nint.Zero)
        {
            TerminateJobObject(_handle, exitCode);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        if (_handle != nint.Zero)
        {
            // Closing the handle triggers KILL_ON_JOB_CLOSE -> tree is terminated by the OS.
            CloseHandle(_handle);
        }
    }

    // ---- Win32 interop ----

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x00002000;
    private const int JobObjectExtendedLimitInformation = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public nuint ProcessMemoryLimit;
        public nuint JobMemoryLimit;
        public nuint PeakProcessMemoryUsed;
        public nuint PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetInformationJobObject(nint hJob, int infoType, nint lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(nint hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(nint hObject);
}
