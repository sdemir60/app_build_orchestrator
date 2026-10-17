using System.ComponentModel;
using System.Runtime.InteropServices;

namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [kaynak şeridi] Bir job'un muhasebesi — <see cref="JobObject.QueryAccounting"/>'in tek çıktısı. <see cref="TotalCpuTime"/> job'un
/// ömrü boyunca harcanan TOPLAM işlemci zamanıdır (kullanıcı + çekirdek; ÇIKMIŞ süreçler dahil — job'dan düşen bir sürecin süresi
/// kaybolmaz), <see cref="ActiveProcesses"/> o an job'da canlı olan süreç sayısıdır.
/// </summary>
public readonly record struct JobAccounting(TimeSpan TotalCpuTime, int ActiveProcesses);

/// <summary>
/// Nested-uyumlu Windows Job Object: KILL_ON_JOB_CLOSE ile son handle kapanışında tüm ağaç ölür.
/// [T20-a/K11] CPU cap ve priority OPT-IN'dir: <see cref="CreateKillOnClose"/> App'in outer job'ı, Supervisor'ın
/// inner job'ı ve testler tarafından ORTAK kullanıldığı için fabrikaya gömülmez — perf profilini uygulamak
/// isteyen çağıran <see cref="SetCpuRate"/>/<see cref="SetPriorityClass"/>'ı ayrıca çağırır.
/// </summary>
public sealed class JobObject : IDisposable, ICpuGovernor
{
    /// <summary><c>JOBOBJECT_BASIC_PROCESS_ID_LIST</c>'in başlığı: iki DWORD (atanmış süreç sayısı, listedeki PID sayısı); girdiler ondan sonra gelir.</summary>
    private const int PidListHeaderBytes = 2 * sizeof(uint);

    private nint _handle;
    private bool _disposed;
    private readonly object _pidListGate = new();
    private byte[]? _pidListBuffer; // QueryProcessIds'in yeniden kullanılan tamponu — _pidListGate altında

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

    /// <summary>
    /// [kaynak şeridi] Job'un muhasebesi: ömrü boyunca harcadığı TOPLAM işlemci zamanı (<c>TotalUserTime + TotalKernelTime</c>; ÇIKMIŞ
    /// süreçler dahil) ve o an canlı üye sayısı. Şeridin CPU yüzdesi bu toplamın iki örnek arasındaki farkından gelir; çıkmış bir
    /// derleyicinin süresi toplamdan düşmez, yani fark eksiye sapmaz. Win32 hatasında (ör. tutamaç kapanmış)
    /// <see cref="Win32Exception"/> fırlar (<see cref="QueryCpuRate"/> kalıbı) — yutmak çağıranın işidir. Isınma sonrası bayt ayırmaz.
    /// </summary>
    public JobAccounting QueryAccounting()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var info = new NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION();
        int size = Marshal.SizeOf<NativeMethods.JOBOBJECT_BASIC_ACCOUNTING_INFORMATION>();
        if (!NativeMethods.QueryInformationJobObject(_handle, NativeMethods.JobObjectBasicAccountingInformation, ref info, size, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());

        // Süreler 100 ns birimindedir = TimeSpan tik'i.
        return new JobAccounting(TimeSpan.FromTicks(info.TotalUserTime + info.TotalKernelTime), (int)info.ActiveProcesses);
    }

    /// <summary>
    /// [kaynak şeridi] Job'un canlı üyelerinin PID'lerini <paramref name="destination"/>'a yazar ve yazılan sayıyı döner (en çok
    /// <c>destination.Length</c>; sıra job'un kendi sırasıdır). Hedef üye sayısından küçükse sığan kadar yazılır: <c>ERROR_MORE_DATA</c> bir
    /// hata DEĞİLDİR, kesilmiş bir okumadır (toplam üye sayısı için <see cref="QueryAccounting"/>'a bakılır). Win32'nin başlığı
    /// iki DWORD'dür, girdiler <c>ULONG_PTR</c>'dir — girdi boyu <see cref="IntPtr.Size"/>'dır (x86'da 4, x64'te 8).
    ///
    /// <para>İç bayt tamponu ilk çağrıda hedefin boyuna göre BİR KEZ ayrılır ve kilit altında yeniden kullanılır (eşzamanlı çağrılar
    /// birbirinin tamponunu bozmaz); yalnız daha büyük bir hedef verilirse büyür, küçülmez. Isınma sonrası, hedef aynı boydaysa, bayt
    /// ayırmaz. Win32 hatasında (ör. tutamaç kapanmış) <see cref="Win32Exception"/> fırlar.</para>
    /// </summary>
    public int QueryProcessIds(int[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Win32 başlığın yanında EN AZ bir girdilik yer ister (yalnız başlık ERROR_BAD_LENGTH verir): boş hedef de bir yuva sorgular,
        // hiçbirini yazmaz.
        int size = PidListHeaderBytes + Math.Max(1, destination.Length) * IntPtr.Size;
        lock (_pidListGate)
        {
            if (_pidListBuffer is null || _pidListBuffer.Length < size) _pidListBuffer = new byte[size];
            byte[] buffer = _pidListBuffer;

            if (!NativeMethods.QueryInformationJobObject(_handle, NativeMethods.JobObjectBasicProcessIdList, buffer, size, out _))
            {
                int error = Marshal.GetLastWin32Error();
                if (error != NativeMethods.ERROR_MORE_DATA) throw new Win32Exception(error);
            }

            // Başlık: [0] NumberOfAssignedProcesses (toplam), [4] NumberOfProcessIdsInList (tampona yazılan). Girdiler 8. bayttan başlar.
            int count = Math.Min(BitConverter.ToInt32(buffer, sizeof(uint)), destination.Length);
            for (int i = 0; i < count; i++)
            {
                int offset = PidListHeaderBytes + i * IntPtr.Size;
                ulong entry = IntPtr.Size == sizeof(ulong) ? BitConverter.ToUInt64(buffer, offset) : BitConverter.ToUInt32(buffer, offset);
                destination[i] = unchecked((int)entry); // PID bir DWORD'dür
            }
            return count;
        }
    }

    // [T20-b/K11] ICpuGovernor: yüzde-ya-da-null ikilisini mevcut iki metoda AÇIK implementasyonla bağlar —
    // JobObject'in public yüzeyine cap yazan üçüncü bir metot EKLENMEZ (tek yazım yolu SetCpuRate/ClearCpuRate).
    void ICpuGovernor.ApplyCap(int? percent)
    {
        if (percent is { } p) SetCpuRate(p);
        else ClearCpuRate();
    }

    void ICpuGovernor.ApplyPriority(ProcessPriorityClassKind kind) => SetPriorityClass(kind);

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
