using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// Nested-uyumlu Windows Job Object: KILL_ON_JOB_CLOSE ile son handle kapanışında tüm ağaç ölür.
/// [T20-a/K11] CPU cap ve priority OPT-IN'dir: <see cref="CreateKillOnClose"/> App'in outer job'ı, Supervisor'ın
/// inner job'ı ve testler tarafından ORTAK kullanıldığı için fabrikaya gömülmez — perf profilini uygulamak
/// isteyen çağıran <see cref="SetCpuRate"/>/<see cref="SetPriorityClass"/>'ı ayrıca çağırır.
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

    /// <summary>
    /// [T20-a/K11] Job'daki TÜM process'lerin priority class'ını tavanlar (Balanced → BelowNormal, Light → Idle).
    /// ⚠️ Priority, <c>JOBOBJECT_EXTENDED_LIMIT_INFORMATION</c>'ı KILL_ON_JOB_CLOSE ile PAYLAŞIR: taze bir struct
    /// kurup yazmak <c>LimitFlags</c>'i sıfırlar ve §3'ün kaskat-kill garantisi SESSİZCE kaybolur (kanıt:
    /// <c>JobCpuRateTests.Priority_write_maps_to_the_win32_class_and_keeps_the_kill_on_job_close_limit_flag</c>).
    /// Bu yüzden yol Query → OR → Set'tir; mevcut limit bayrakları korunur.
    /// </summary>
    public void SetPriorityClass(ProcessPriorityClassKind kind)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        uint priorityClass = kind switch
        {
            ProcessPriorityClassKind.Normal => NativeMethods.NORMAL_PRIORITY_CLASS,
            ProcessPriorityClassKind.BelowNormal => NativeMethods.BELOW_NORMAL_PRIORITY_CLASS,
            ProcessPriorityClassKind.Idle => NativeMethods.IDLE_PRIORITY_CLASS,
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        var info = new NativeMethods.JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        if (!NativeMethods.QueryInformationJobObject(_handle, NativeMethods.JobObjectExtendedLimitInformation, ref info, size, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        info.BasicLimitInformation.LimitFlags |= NativeMethods.JOB_OBJECT_LIMIT_PRIORITY_CLASS; // OR — silme YOK
        info.BasicLimitInformation.PriorityClass = priorityClass;

        if (!NativeMethods.SetInformationJobObject(_handle, NativeMethods.JobObjectExtendedLimitInformation, ref info, size))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>
    /// [T20-a/K11] Job'a HARD_CAP'li CPU rate limiti uygular (Balanced %70 · Light %40). Ayrı bir
    /// info-class (15) üzerinden gider, <c>ExtendedLimitInformation</c>'a — dolayısıyla KILL_ON_JOB_CLOSE'a —
    /// DOKUNMAZ. Cap, job'daki tüm process'lerin TOPLAMI için makinenin toplam CPU'sunun yüzdesidir.
    /// </summary>
    /// <param name="percent">1..100 arası yüzde.</param>
    public void SetCpuRate(int percent)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);

        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION
        {
            ControlFlags = NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE | NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP,
            CpuRate = (uint)(percent * 100), // birim: 1/100 yüzde
        };
        SetCpuRateControl(ref info);
    }

    /// <summary>[T20-a/K11] Cap'i tamamen kaldırır (Full modu ve run sonu geri alma). ControlFlags=0 ⇒
    /// CPU rate control kapalı — cap'siz job ile ayırt edilemez hale gelir.</summary>
    public void ClearCpuRate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION(); // ControlFlags=0 ⇒ rate control kapalı
        SetCpuRateControl(ref info);
    }

    /// <summary>
    /// [T20-a] Yürürlükteki HARD CAP'i yüzde olarak okur; cap yoksa <c>null</c>. Doğrulama seam'i:
    /// hem testler hem It-5 acceptance kanıtı bunu kullanır.
    /// <para>Yalnız <c>ENABLE | HARD_CAP</c> kombinasyonu "cap" sayılır. Gerekçe: <c>ControlFlags</c>'ten
    /// sonraki 4 bayt bir UNION'dır — weight-based (<c>Weight</c>) veya min/max-rate modunda o alan cap
    /// DEĞİLDİR; yalnız ENABLE bitine bakıp alanı koşulsuz <c>CpuRate</c> diye yorumlamak o modlarda uydurma
    /// bir "cap" raporlardı.</para>
    /// </summary>
    public int? QueryCpuRate()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var info = new NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION();
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
        if (!NativeMethods.QueryInformationJobObject(_handle, NativeMethods.JobObjectCpuRateControlInformation, ref info, size, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        const uint hardCap = NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_ENABLE
            | NativeMethods.JOB_OBJECT_CPU_RATE_CONTROL_HARD_CAP;
        if ((info.ControlFlags & hardCap) != hardCap) return null;
        return (int)(info.CpuRate / 100);
    }

    private void SetCpuRateControl(ref NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION info)
    {
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_CPU_RATE_CONTROL_INFORMATION>();
        if (!NativeMethods.SetInformationJobObject(_handle, NativeMethods.JobObjectCpuRateControlInformation, ref info, size))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        NativeMethods.CloseHandle(_handle); // son handle kapanışı → KILL_ON_JOB_CLOSE kaskadı
        _handle = nint.Zero;
    }
}
