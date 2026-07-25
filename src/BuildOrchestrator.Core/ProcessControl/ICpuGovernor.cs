namespace BuildOrchestrator.Core.ProcessControl;

/// <summary>
/// [T20-b/K11] Bir perf profilinin (<see cref="PerfProfile"/>) CPU cap + priority yarısını UYGULAYAN ince seam.
/// Tek üretim implementasyonu <see cref="JobObject"/>'tir (açık implementasyon — <c>SetCpuRate</c>/
/// <c>ClearCpuRate</c>/<c>SetPriorityClass</c>'ın ikinci bir kopyası public yüzeye eklenmez).
///
/// <para><b>Neden ayrı bir arayüz:</b> <c>RunCoordinator</c> cap'i somut Job'a değil buna uygular; böylece
/// "run başında uygulandı / koşarken değişti / run sonunda geri alındı" SIRASI gerçek bir Win32 job olmadan
/// doğrulanabilir. <c>JobObject.QueryCpuRate</c> yalnız YÜRÜRLÜKTEKİ durumu gösterir, sırayı değil.</para>
///
/// <para><b>Kime uygulanır:</b> yalnız Supervisor'ın INNER job'ına — orada yalnız <c>MSBuild.exe</c> child'ları
/// yaşar. App'in OUTER job'ına ASLA (orası Supervisor'ın kendisini, dolayısıyla IPC'yi de kısardı). Yan not:
/// git/vswhere child'ları düz <c>Process.Start</c> ile doğar ve inner job'a assign EDİLMEZ (bkz.
/// <c>Core.Processes.ProcessRunner</c>) — bu yüzden cap derlemeyi kısar, git'i kısmaz.</para>
/// </summary>
public interface ICpuGovernor
{
    /// <summary>Hard CPU cap uygular; <paramref name="percent"/> <c>null</c> ise cap'i tamamen KALDIRIR
    /// (Full profili ve run sonu geri alma).</summary>
    /// <param name="percent">1..100 arası yüzde ya da <c>null</c> (cap yok).</param>
    void ApplyCap(int? percent);

    /// <summary>Job'daki tüm process'lerin priority class'ını tavanlar. §3 garantisi (KILL_ON_JOB_CLOSE)
    /// BOZULMAZ — bkz. <see cref="JobObject.SetPriorityClass"/>'ın Query→OR→Set yolu.</summary>
    void ApplyPriority(ProcessPriorityClassKind kind);
}
