using System.Runtime.InteropServices;
using System.Text;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// Win32 P/Invoke gövdesi — Job Object + IOCP + suspended-launch. Kanıtlanmış spike harness'ından
/// (.claude/temp/spike/jobspike/Program.cs) birebir port edilmiştir.
/// </summary>
internal static class NativeMethods
{
    public const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;
    // [T20-a/K11] Priority, KILL_ON_JOB_CLOSE ile AYNI struct'ta (JOBOBJECT_EXTENDED_LIMIT_INFORMATION) yaşar —
    // LimitFlags üzerine körlemesine yazan her çağrı §3 kaskat garantisini siler (bkz. JobObject.SetPriorityClass).
    public const uint JOB_OBJECT_LIMIT_PRIORITY_CLASS = 0x20;
    public const int JobObjectExtendedLimitInformation = 9;
    public const int JobObjectAssociateCompletionPortInformation = 7;
    // [T20-a/K11] CPU rate ise AYRI bir info-class'tır — ExtendedLimitInformation'a hiç dokunmaz, güvenlidir.
    public const int JobObjectCpuRateControlInformation = 15;
    public const uint JOB_OBJECT_CPU_RATE_CONTROL_ENABLE = 0x1;
    public const uint JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP = 0x4;

    // Win32 priority class değerleri (CreateProcess dwCreationFlags ile aynı sabitler).
    public const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    public const uint IDLE_PRIORITY_CLASS = 0x00000040;
    public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;

    public const uint CREATE_SUSPENDED = 0x00000004;
    public const uint CREATE_NO_WINDOW = 0x08000000;
    public const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    public const uint CREATE_BREAKAWAY_FROM_JOB = 0x01000000;

    public const uint STARTF_USESTDHANDLES = 0x00000100;

    public const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public static readonly nint PROC_THREAD_ATTRIBUTE_HANDLE_LIST = new(0x00020002);

    public const uint JOB_OBJECT_MSG_ACTIVE_PROCESS_ZERO = 4;
    public const uint JOB_OBJECT_MSG_NEW_PROCESS = 6;
    public const uint JOB_OBJECT_MSG_EXIT_PROCESS = 7;
    public const uint JOB_OBJECT_MSG_ABNORMAL_EXIT_PROCESS = 8;

    public const uint INFINITE = 0xFFFFFFFF;
    public const int WAIT_TIMEOUT = 0x102;

    public static readonly nint INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    /// <summary>
    /// [T20-a] Win32 karşılığında <c>ControlFlags</c>'ten sonraki 4 bayt bir UNION'dır
    /// (<c>CpuRate</c> | <c>Weight</c> | <c>{MinRate, MaxRate}</c>). Bu kod YALNIZ hard-cap yolunu kullandığı
    /// için union'ın tek üyesi (<c>CpuRate</c>) modellenir — diğer üyeler ihtiyaç doğduğunda eklenir; Explicit
    /// layout, o alanın union yuvası olduğunu belgelemek için korunur. Struct boyutu 8 bayttır ve
    /// <c>cbJobObjectInfoLength</c> tam bunu bildirmelidir (yanlış boyut ⇒ P/Invoke Win32 hatasıyla döner;
    /// ölçülen: <c>ERROR_BAD_LENGTH</c> 24). <c>CpuRate</c> birimi 1/100 yüzdedir: %70 → 7000.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 8)]
    public struct JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
    {
        [FieldOffset(0)] public uint ControlFlags;
        [FieldOffset(4)] public uint CpuRate; // union yuvası: hard-cap modunda CpuRate
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct JOBOBJECT_ASSOCIATE_COMPLETION_PORT
    {
        public nint CompletionKey;
        public nint CompletionPort;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct STARTUPINFOW
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public uint dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2;
        public nint hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_INFORMATION
    {
        public nint hProcess;
        public nint hThread;
        public uint dwProcessId;
        public uint dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct STARTUPINFOEXW
    {
        public STARTUPINFOW StartupInfo;
        public nint lpAttributeList;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern nint CreateJobObjectW(nint lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(nint hJob, int jobObjectInfoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(nint hJob, int jobObjectInfoClass,
        ref JOBOBJECT_ASSOCIATE_COMPLETION_PORT lpJobObjectInfo, int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetInformationJobObject(nint hJob, int jobObjectInfoClass,
        ref JOBOBJECT_CPU_RATE_CONTROL_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryInformationJobObject(nint hJob, int jobObjectInfoClass,
        ref JOBOBJECT_EXTENDED_LIMIT_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength, out uint lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool QueryInformationJobObject(nint hJob, int jobObjectInfoClass,
        ref JOBOBJECT_CPU_RATE_CONTROL_INFORMATION lpJobObjectInfo, int cbJobObjectInfoLength, out uint lpReturnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateJobObject(nint hJob, uint uExitCode);

    // [Task 18] Task 15'te GetACP() kaldırıldı; burada da yalnız STARTUPINFOEXW overload'ı canlı yol tarafından
    // kullanılıyor (bkz. JobProcessLauncher — `six` her zaman STARTUPINFOEXW) — düz STARTUPINFOW alan overload
    // hiçbir çağıranı olmadığı için (grep doğrulaması) kaldırıldı.
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateProcessW(
        string? lpApplicationName, StringBuilder lpCommandLine, nint lpProcessAttributes, nint lpThreadAttributes,
        bool bInheritHandles, uint dwCreationFlags, nint lpEnvironment, string? lpCurrentDirectory,
        ref STARTUPINFOEXW lpStartupInfo, out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(nint lpAttributeList, int dwAttributeCount, int dwFlags, ref nint lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(nint lpAttributeList, uint dwFlags, nint attribute,
        nint lpValue, nint cbSize, nint lpPreviousValue, nint lpReturnSize);

    [DllImport("kernel32.dll")]
    public static extern void DeleteProcThreadAttributeList(nint lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint ResumeThread(nint hThread);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(nint hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(nint hProcess, uint uExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern nint CreateIoCompletionPort(nint fileHandle, nint existingCompletionPort, UIntPtr completionKey, uint numberOfConcurrentThreads);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetQueuedCompletionStatus(nint completionPort, out uint lpNumberOfBytes, out UIntPtr lpCompletionKey, out nint lpOverlapped, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetExitCodeProcess(nint hProcess, out uint lpExitCode);

    // Fix wave 1 / Finding 4 (MsBuildOutputEncoding) — [Task 15] KALDIRILDI: GetACP()/ANSI-codepage-decode
    // varsayımı bu toolchain'de (VS18/Roslyn UTF-8 yazıyor) mojibake üretiyordu; MsBuildOutputEncoding artık
    // pure UTF-8 kullanıyor ve bu P/Invoke'a bağımlı değil (bkz. MsBuildOutputEncoding.cs XML doc, "D-Task15").
}
