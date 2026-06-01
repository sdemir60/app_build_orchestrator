using System.Runtime.InteropServices;

namespace BuildOrchestrator.Worker.ProcessControl;

/// <summary>
/// Windows Job Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE (Section 6.1, rule 1).
/// The worker process and every child process (MSBuild node, VBCSCompiler, build events)
/// are bound to this job. If the worker dies for ANY reason (normal exit, crash, Task Manager
/// kill), Windows automatically terminates the whole process tree — orphans are impossible.
/// </summary>
public sealed class JobObject : IDisposable
{
    private readonly nint _handle;
    private bool _disposed;

    public bool IsValid => _handle != nint.Zero;

    public JobObject()
    {
        _handle = CreateJobObject(nint.Zero, null);
        if (_handle == nint.Zero)
            return;

        var info = new JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        };
        var extended = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION { BasicLimitInformation = info };

        int length = Marshal.SizeOf(extended);
        nint ptr = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extended, ptr, false);
            if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ptr, (uint)length))
            {
                CloseHandle(_handle);
                _handle = nint.Zero;
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    /// <summary>Assign a process (by handle) to this job. Children inherit job membership.</summary>
    public bool Assign(nint processHandle)
        => IsValid && AssignProcessToJobObject(_handle, processHandle);

    /// <summary>Bind the current process so all of its descendants are killed on job close.</summary>
    public bool AssignCurrentProcess()
    {
        using var current = System.Diagnostics.Process.GetCurrentProcess();
        return Assign(current.Handle);
    }

    /// <summary>Force-terminate all processes in the job (used by hard Stop, Section 6.1 rule 2).</summary>
    public void TerminateAll(uint exitCode = 1)
    {
        if (IsValid)
            TerminateJobObject(_handle, exitCode);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_handle != nint.Zero)
            CloseHandle(_handle); // triggers KILL_ON_JOB_CLOSE
    }

    // ---- P/Invoke ----

    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    private const int JobObjectExtendedLimitInformation = 9;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateJobObject(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(nint hJob, int infoClass, nint lpInfo, uint cbInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(nint hJob, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);

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
}
