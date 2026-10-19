using System.ComponentModel;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.App.Services;

/// <summary>
/// [kaynak şeridi · Exit · inceleme O2] App'in outer job'unun canlı üyelerinin PID'leri — job'u okuyan iki yüzeyin ORTAK iskeleti: kaynak
/// şeridinin okuyucusu (<see cref="JobResourceProbe"/>) ve Exit kartının sayımı (<see cref="JobProcessCensus"/>). Motora IPC yoktur ve
/// makinedeki süreçler taranmaz (<c>Process.GetProcesses()</c> yasak — <c>NoProcessScanGuardTests</c>): job'un kendi PID listesi okunur.
///
/// <para>Tampon <see cref="Capacity"/> girdiyle sınırlıdır (<see cref="JobObject.QueryProcessIds"/> hedef küçükse sessizce keser): bir koşunun
/// job'u onlarca süreçtir, 256 geniş bir paydır. Tampon bir kez kurulur ve yeniden kullanılır — ısınma sonrası okuma başına bayt ayırmaz
/// (<c>ResourceSamplerTests</c> şeridin yolunu pinler). Yalnız UI thread'inden çağrılır.</para>
/// </summary>
internal sealed class OuterJobMembers
{
    /// <summary>PID tamponunun boyu.</summary>
    internal const int Capacity = 256;

    private readonly JobObject _job;
    private readonly int[] _processIds = new int[Capacity];

    /// <param name="job">App'in outer job'u (<c>EngineHost.OuterJob</c>) — yalnız OKUNUR.</param>
    public OuterJobMembers(JobObject job)
    {
        ArgumentNullException.ThrowIfNull(job);
        _job = job;
    }

    /// <summary>Job okunabildiyse <c>true</c> ve üyelerin PID'leri (tamponun bir dilimi — bir sonraki okumaya dek geçerlidir). Job sorgusu
    /// düşerse (<see cref="Win32Exception"/> — ör. tutamaç kapanmış; <see cref="ObjectDisposedException"/> — uygulama kapanırken motor konağı
    /// dispose edilmiş) <c>false</c>; FIRLATMAZ.</summary>
    public bool TryRead(out ReadOnlySpan<int> processIds)
    {
        try
        {
            int count = _job.QueryProcessIds(_processIds);
            processIds = _processIds.AsSpan(0, count);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
            processIds = default;
            return false;
        }
    }
}
