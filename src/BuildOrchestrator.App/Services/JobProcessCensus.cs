using System.ComponentModel;
using BuildOrchestrator.Core.ProcessControl;

namespace BuildOrchestrator.App.Services;

/// <summary>[Exit] Onay kartının "ENDS ON EXIT" listesinin bir satırı: süreç adı ve kaç tane olduğu.</summary>
internal readonly record struct ProcessTally(string Name, int Count);

/// <summary>
/// [Exit] Onay kartının "ENDS ON EXIT" listesi — çıkışın bitireceği süreçler. Kaynak App'in outer job'udur (Supervisor ağacının tamamı:
/// motor, git, vswhere, MSBuild ve onların çocukları): job'un PID listesi okunur ve her PID tek tek adlandırılır
/// (<see cref="ProcessImage.NameOf"/>). Motora IPC YOKTUR ve makinedeki süreçler TARANMAZ — <see cref="JobResourceProbe"/>'un deseni.
///
/// <para><b>Listede olmayanlar:</b> motorun kendisi (uygulamanın parçasıdır; tasarımın listesi işi yapan süreçleri sayar ve "gösterilecek
/// süreç yoksa blok görünmez" der — motor sayılsaydı blok hiç kaybolmazdı) ve konsol konakları (<c>conhost.exe</c> — job'a motorun ve
/// çocuklarının konsolu olarak girer, iş yapmaz). Çıkmış ya da açılamayan süreç sayılmaz.</para>
///
/// <para>Yalnız kart açıkken okunur (kabuğun zamanlayıcısı); boşta hiçbir şey sorgulanmaz. Job okunamazsa (Win32 hatası, kapanmış tutamaç —
/// uygulama kapanırken motor konağı dispose edilmiş) liste boştur ve istisna ÇIKMAZ: kart süreç bloğunu çizmez.</para>
/// </summary>
internal sealed class JobProcessCensus
{
    /// <summary>PID tamponunun boyu — <see cref="JobResourceProbe.ProcessIdCapacity"/> ile AYNI pay (job'u aynı sorgu okur).</summary>
    private const int ProcessIdCapacity = JobResourceProbe.ProcessIdCapacity;

    /// <summary>Konsol konağının image adı — iş yapmaz, listede yoktur.</summary>
    private const string ConsoleHost = "conhost.exe";

    private readonly JobObject _outerJob;
    private readonly Func<int?> _enginePid;
    private readonly int[] _processIds = new int[ProcessIdCapacity];

    /// <param name="outerJob">App'in outer job'u (<c>EngineHost.OuterJob</c>) — yalnız OKUNUR.</param>
    /// <param name="enginePid">Motor sürecinin PID'i (<c>EngineHost.EnginePid</c>); motor uyuyorsa/yoksa <c>null</c>. Her okumada sorulur.</param>
    public JobProcessCensus(JobObject outerJob, Func<int?> enginePid)
    {
        ArgumentNullException.ThrowIfNull(outerJob);
        ArgumentNullException.ThrowIfNull(enginePid);
        _outerJob = outerJob;
        _enginePid = enginePid;
    }

    /// <summary>Şu anki liste (<see cref="Tally"/>'nin sırasıyla). Job okunamazsa boş.</summary>
    public IReadOnlyList<ProcessTally> Take()
    {
        int count;
        try
        {
            count = _outerJob.QueryProcessIds(_processIds);
        }
        catch (Exception ex) when (ex is Win32Exception or ObjectDisposedException)
        {
            return [];
        }

        int? enginePid = _enginePid();
        var names = new List<string>(count);
        for (int i = 0; i < count; i++)
        {
            int pid = _processIds[i];
            if (pid == enginePid) continue;
            if (ProcessImage.NameOf(pid) is { } name) names.Add(name);
        }
        return Tally(names);
    }

    /// <summary>Adları sayar: büyük/küçük harf ayrımsız gruplar (ilk görülen yazılış kalır), konsol konaklarını atlar; en kalabalık önce,
    /// eşitlikte ada göre (büyük/küçük harf ayrımsız).</summary>
    internal static IReadOnlyList<ProcessTally> Tally(IEnumerable<string> names) =>
    [
        .. names
            .Where(n => !n.Equals(ConsoleHost, StringComparison.OrdinalIgnoreCase))
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ProcessTally(g.First(), g.Count()))
            .OrderByDescending(t => t.Count)
            .ThenBy(t => t.Name, StringComparer.OrdinalIgnoreCase),
    ];
}
