using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.Scheduling;
using BuildOrchestrator.Core.State;

namespace BuildOrchestrator.Supervisor;

/// <summary>
/// Bir run'ın planı: <see cref="BuildPlan"/> (build-order'da) + her projenin solution referansları
/// (<c>SolutionDirResolver</c> için gereklidir; <see cref="ProjectNode.SolutionNames"/> yalnız AD taşır, YOL taşımaz).
/// Planlama TAMAMEN Core'da yapılır [D3]; koordinatör yalnız çalıştırır — bu tip iki Core çıktısını bir arada taşır.
/// </summary>
public sealed record RunPlan(BuildPlan Plan, IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>> SolutionRefs,
    IncrementalPlan? Incremental = null);

/// <summary>
/// [Task 19 wiring] Bir fresh (Rebuild/Build) run için incremental karar verileri: her projenin planlama
/// anında hesaplanmış <see cref="Contracts.Model.BuildSignature"/> (byte-stable) imzası + HEAD commit + branch.
/// <see cref="RunCoordinator"/> bir proje <c>projectSucceeded</c> olduğunda bu bilgiyle <see
/// cref="Core.State.BuildStateStore"/>'a <see cref="BuildState"/> persist eder — böylece BİR SONRAKİ Build
/// incremental olur. <see cref="SignatureById"/> yalnız non-null imzaları içerir (hollow/never-committed
/// persist edilmez); <c>null</c> Incremental (ör. testlerdeki basit planner) → persist YOK, pre-skip YOK.
/// [A2 fix-1] Bu yalnız BAŞARI yolu içindir: başarısızlıkta yapılan invalidasyon (bkz.
/// <c>InvalidateBuildStateOnFailure</c>) imza/HEAD gerektirmez, mevcut kaydı yerinde günceller.
/// </summary>
public sealed record IncrementalPlan(
    IReadOnlyDictionary<string, string> SignatureById,
    string? HeadCommit,
    string? Branch);

/// <summary>
/// Bir run için MSBuild takımı: <b>ham</b> (retry'siz) invoker + çözülmüş MSBuild.exe yolu.
/// Yol, proje logunun İLK satırına yazılan gerçek komut satırını üretmek için gerekir (v7Δ-7).
/// Retry sarmalamasını (<see cref="RetryingMsBuildInvoker"/>) koordinatör yapar: <c>onRetry</c> run'a özgü
/// <c>decision.log</c>'a yazar, o log ise ancak run başlarken var olur.
/// </summary>
public sealed record MsBuildToolset(IMsBuildInvoker Invoker, string MsBuildExePath);

/// <summary>
/// [T4/T55] Run'ın yürütme kalbi: plan → N paralel worker → proje-başına <c>MSBuild.exe</c> shell-out →
/// disk log + IPC event → Stop/Continue. Planlama YOK (Core'un işi [D3]), in-process MSBuild YOK [§0/§3],
/// bin/OutDir okuma YOK [§4], bellek ring buffer YOK — tek log kaynağı disktir [D4].
///
/// <para><b>Tek seferde tek run</b> (A6): koşarken gelen <c>startRun</c> → <c>error(runInProgress)</c>.</para>
///
/// <para><b>Worker döngüsü:</b> N worker aynı <see cref="ReadySetScheduler"/>'ı sürer.
/// <c>TryDispatch == false</c> "run bitti" DEĞİL, "şu an hazır iş yok" demektir (bağımlılıkları hâlâ
/// derleniyor olabilir); bu yüzden döngü <see cref="ReadySetScheduler.IsDone"/> olana kadar sürer ve hazır iş
/// yokken <see cref="WakeSignal"/> üzerinde PARK eder. Her <c>Complete</c>'ten (ve her Stop'tan) sonra tüm
/// parked worker'lar uyandırılır — sleep-poll YOK [D8]. Sinyal, beklenecek Task <i>koşul kontrolünden ÖNCE</i>
/// yakalanarak kaçırılmaz (lost-wakeup yok).</para>
///
/// <para><b>Event sırası:</b> tüm event'ler tek bir FIFO kanaldan tek bir pump task'ı ile yazılır. Sebep:
/// <c>onLine</c> SENKRON çağrılır (MSBuild'in stdout/stderr pump thread'lerinden) ama IPC yazımı async'tir —
/// kanal hem sırayı garanti eder (<c>runStarted</c> → <c>projectStarted</c>* → sonuç* → <c>runCompleted</c>)
/// hem de pump thread'ini BLOKLAMAZ (bkz. MsBuildInvoker: terk edilmiş pump'lar zaten thread-pool baskısı
/// yaratıyor; buraya bloklu bekleme eklenmez).</para>
/// </summary>
/// <param name="planner">(komut, ilerleme kanalı) → <see cref="RunPlan"/>. Core'un planlama pipeline'ı; senkron ve
/// I/O yapar, bu yüzden run'ın arka plan task'ından çağrılır (IPC dispatch loop'u bloklanmaz).
/// <para>[planlama görünürlüğü] İkinci parametre, planlayıcının adım satırlarını yayınladığı kanaldır: her satır
/// <see cref="PlanProgressEvent"/> olarak, <c>runStarted</c>'tan ÖNCE App'e gider. Bu pencere 177 projelik bir
/// workspace'te saniyeler sürer ve eskiden TEK event bile üretmiyordu — App konsolu temizleyip
/// <c>IsStarting</c>'e giriyor, şerit önceki metinde donuyordu. Kanal SENKRON çağrılabilir (kanal yazımı
/// bloklamaz) ve yalnız TAZE segmentte kullanılır: Continue/RetryFailed planner'ı hiç çağırmaz.</para></param>
/// <param name="msbuildFactory">MSBuild takımını (ham invoker + exe yolu) LAZY çözer: vswhere/VS yoksa Supervisor
/// yine ayağa kalkar, hata ancak <c>startRun</c>'da <c>error(msbuildNotFound)</c> olarak bildirilir.</param>
/// <param name="logFactory">Run başına TEK <see cref="RunLogWriter"/> üretir; Continue AYNI writer'ı (aynı run
/// dizinini) kullanır — log dikişi bozulmaz.</param>
/// <param name="nowMs">MONOTONİK zaman kaynağı (üretimde <c>Environment.TickCount64</c>); duvar saati
/// KULLANILMAZ — geri atlarsa elapsed negatife düşerdi.</param>
/// <param name="console">Konsol (stderr) uyarı/özet kanalı. stdout YALNIZ NDJSON'dır [D4], bu yüzden buradan
/// asla stdout'a yazılmaz.</param>
/// <param name="worktreeObjRootResolver">
/// [I2-K2/It-3 Task 10 · A4] <c>cmd.UseWorktree</c>=true iken bu run için kullanılacak worktree kökünü döner
/// (null dönerse in-place gibi davranılır — obj izole EDİLMEZ). [A4] Worktree'nin GERÇEKTEN hazırlanması
/// (<c>WorktreeManager.PrepareWorktreeAsync</c>) Program.cs'te <c>planner</c>'ın İÇİNDE yapılır; bu resolver
/// yalnız orada çözülmüş kökü okur ve bu yüzden <b>planner'dan SONRA</b> çağrılır. YALNIZ taze (Rebuild/Build)
/// segmentte çağrılır: Continue/RetryFailed orijinal run'ın kökünü miras alır (bkz. <c>_worktreeObjRoot</c>).
/// Parametre isteğe bağlıdır; verilmezse (varsayılan <c>null</c>) her zaman in-place obj kullanılır.
/// Verildiğinde, dönen kök
/// <see cref="Core.MsBuild.WorktreeObjPathResolver.Resolve"/> ile proje-Id başına izole bir
/// <c>BaseIntermediateOutputPath</c>'e çevrilir — obj PAYLAŞILMAZ (bayat-obj zehri, SPIKE-proven
/// OSYS.Types.NewSales.Print vakası).
/// </param>
/// <param name="cpuGovernor">
/// [T20-b/K11] Perf profilinin CPU cap + priority yarısının uygulandığı seam. Varsayılan (null) ⇒
/// <paramref name="innerJob"/>'ın KENDİSİ — yani cap DAİMA yalnız inner job'a uygulanır (App'in outer job'ına
/// ASLA: orası Supervisor'ın kendisini ve IPC'yi kısardı). Ayrı bir parametre olmasının TEK sebebi test
/// edilebilirliktir: cap/priority çağrılarının SIRASI (run başı → canlı değişim → run sonu geri alma) gerçek
/// bir Win32 job üzerinden gözlenemez. Terminate hâlâ <paramref name="innerJob"/>'ın işidir — o, cap'ten
/// bağımsız bir yaşam-döngüsü yetkisidir.
/// </param>
/// <param name="retryDelay">
/// [T20-b/P3 · D8] Copy-contention retry'ının backoff bekleyişi (bkz. <see cref="RetryingMsBuildInvoker"/>).
/// Üretimde <c>Task.Delay</c>'dir; testlerde anında tamamlanan bir callback verilir — retry SIRASI (ve onunla
/// birlikte cap taban penceresinin açılıp kapanması) gerçek zaman beklenmeden doğrulanabilsin diye. Varsayılan
/// (null) ⇒ <c>Task.Delay</c>.
/// </param>
public sealed class RunCoordinator(
    Func<StartRunCommand, Action<string>, RunPlan> planner,
    Func<CancellationToken, Task<MsBuildToolset>> msbuildFactory,
    Func<DateTimeOffset, RunLogWriter> logFactory,
    NdjsonWriter writer,
    JobObject innerJob,
    Func<long> nowMs,
    Action<string> console,
    Func<StartRunCommand, string?>? worktreeObjRootResolver = null,
    BuildStateStore? stateStore = null,
    ICpuGovernor? cpuGovernor = null,
    Func<TimeSpan, CancellationToken, Task>? retryDelay = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly ICpuGovernor _cpu = cpuGovernor ?? innerJob;

    // --- run yaşam döngüsü (hepsi _gate altında) ---
    private bool _runActive;            // startRun slotu dolu (planlama dahil) — A6
    private bool _finishing;            // sonuç olayları yazılmaya başladı → Stop artık sahiplenilemez
    private Task _runTask = Task.CompletedTask;
    private StopKind? _stopKind;        // null = stop istenmedi; Hard, Graceful'u EZER (geri alınmaz)
    private bool _stopAcked;            // runStopped yazıldı mı — TryRequestStop true dedi ise ACK BORCU vardır
    private ReadySetScheduler? _scheduler;
    private WakeSignal? _wake;
    // [T20-b/K11] Bu run için cap/priority GERÇEKTEN uygulandı mı (yani komut bir PerfMode taşıyor muydu).
    // Yalnız true iken run sonunda geri alınır ve yalnız true iken canlı setPerfMode UYGULANIR: PerfMode
    // taşımayan (P2 öncesi / harness) run'lar job'a hiç dokunmamalıdır.
    private bool _perfApplied;
    // [Fix round 1 — KÖK 1] PLANLAMA PENCERESİNDE gelmiş perf niyeti. startRun kabul edilir edilmez _runActive
    // true olur, ama cap ancak plan kurulduktan SONRA (RunSegmentAsync'in apply noktası) uygulanır; 177 projede
    // bu pencere SANİYELERDİR ve perf chip'i o sırada canlıdır. Buraya yazılan niyet run başlarken komuttaki
    // PerfMode'u EZER (kullanıcının SON sözü). Apply noktasında tüketilir; [Fix round 2 — YENİ 1] run
    // kapanışında İKİ kez temizlenir (ReleasePerf + son kilit), çünkü ikisinin arasındaki `await pump`
    // penceresinde _runActive hâlâ true'dur ve oraya düşen bir niyeti başka temizleyen olmazdı.
    private PerfProfile? _pendingPerf;
    // [T20-b/P3] Bu run'da YÜRÜRLÜKTEKİ profil — copy-contention penceresi kapanınca cap buraya döner.
    // (_perfApplied false iken anlamsızdır; ikisi de run sınırında birlikte sıfırlanır.)
    private PerfProfile? _activePerf;
    // [T20-b/P3] Şu an KAÇ worker copy-contention penceresinde. Ref-count zorunludur: paralel build'de birden
    // çok worker aynı anda MSB302x görebilir ve erken çıkan biri, diğeri hâlâ kopyalarken cap'i geri kısardı.
    private int _copyFloorDepth;
    // [T20-b/P3] Graceful drain başladı ⇒ bu run'da cap bir daha YAZILMAZ ve [final review I-1] priority taban
    // değerin altına İNDİRİLEMEZ (pencere kapanışı ve canlı setPerfMode dahil). "torn DLL yok" garantisi,
    // sonradan geri konan bir cap ya da Idle'a düşürülen bir priority ile pazarlık edilemez.
    private bool _capDrained;

    // --- Continue için devredilen state (run'lar ARASINDA yaşar) ---
    private RunPlan? _plan;
    private string? _root;
    private RunLogWriter? _logs;
    // [A4] Bu run için ÇÖZÜLMÜŞ worktree obj kökü (null ⇒ in-place). Taze (Rebuild/Build) segmentte BİR KEZ
    // resolver'dan hesaplanır; Continue/RetryFailed segmentleri komuttan YENİDEN hesaplamaz, buradan MİRAS
    // ALIR — App Continue'yu UseWorktree=false ile yolladığı için (RunViewModel) aksi halde aynı run'ın
    // yarısı worktree'nin izole obj'sine, yarısı projenin default obj'sine derlenirdi.
    private string? _worktreeObjRoot;
    private RunSnapshot? _snapshot;
    // [T54] projectId → o projenin (dependency zincirinden) taşıdığı kök depIssue adları. Succeeded/Failed
    // tallies gibi run SEGMENTLERİ ARASINDA KÜMÜLATİF: Continue AYNI birikimi devralır (aksi halde 1. segmentte
    // tamamlanmış bir projenin depIssue zinciri, 2. segmentteki dependent'ları için kaybolurdu).
    private ConcurrentDictionary<string, IReadOnlyList<string>>? _depIssuesById;
    // [Task-13] projectId → Failed'a düştüğü AN ki reason "stopped" mıydı (torn-DLL guard). RunSnapshot/BuildResult
    // reason TAŞIMAZ — bu yüzden reason bilgisi ayrı, run segmentleri arası kümülatif bu sözlükte izlenir (aynı
    // _depIssuesById gibi Continue/RetryFailed segmentleri BOYUNCA aynı örnek paylaşılır). Bir proje sonradan
    // FARKLI bir sonuçla (Succeeded ya da başka reason'la Failed) tamamlanırsa buradan silinir (bkz.
    // BuildProjectAsync). NOT: RetryPlanning re-queue bir girdiyi Queued'a çevirirken bu seti güncellemez, bu
    // yüzden geçici olarak bayat bir "stopped" girdisi kalabilir; RequeueStoppedFailed'ın savunmacı re-check'i
    // (yalnız hâlâ Failed olanları re-queue eder) bunu zararsız kılar.
    private ConcurrentDictionary<string, byte>? _stoppedFailedIds;
    // [T28] En son (aktif ya da tamamlanmış) run'ın dizini — _logs Dispose edilip null'landıktan SONRA da
    // hayatta kalır: run tamamen bitmiş olsa bile bir proje kartına tıklamak logunu diskten okuyabilsin diye.
    private string? _lastRunDirectory;

    private bool _disposed;

    /// <summary>Aktif (ya da en son) run'ın task'ı: run'ın TÜM event'leri yazıldıktan sonra tamamlanır.</summary>
    public Task RunCompletion { get { lock (_gate) return _runTask; } }

    /// <summary>Stop'la yarıda kalmış, Continue ile sürdürülebilir bir run var mı (kuyrukta iş kaldı mı).</summary>
    public bool HasResumableRun { get { lock (_gate) return HasResumableRunLocked; } }

    private bool HasResumableRunLocked =>
        _snapshot is not null && _plan is not null && _logs is not null && _snapshot.Queued.Count > 0;

    /// <summary>
    /// [Task-13] RetryFailed'a açık bir run var mı: en az bir Failed proje taşıyan bir snapshot/plan/logs
    /// devredilmiş olmalı. <see cref="HasResumableRunLocked"/>'dan FARKLI: Continue "Queued backlog var mı"
    /// sorar (yalnız Stopped+Queued&gt;0), bu ise "Failed proje var mı" sorar — bir run TAMAMEN bitmiş
    /// (Completed, Queued=0) olsa bile içinde Failed projeler varsa RetryFailed'a açıktır (bkz.
    /// RunSegmentAsync'in finally'sindeki <c>resumable</c> hesaplaması — plan/logs bu durumda da devredilir).
    /// </summary>
    private bool HasRetryableRunLocked =>
        _snapshot is not null && _plan is not null && _logs is not null
        && _snapshot.Completed.Values.Any(v => v == BuildResult.Failed);

    /// <summary>
    /// [T28] <c>getProjectLog</c>'un tek kaynağı. Aktif/resumable run varsa canlı writer'dan (in-memory sayaçla
    /// ATOMİK — bkz. <see cref="RunLogWriter.SnapshotProjectLog"/>) okunur; run tamamen bitmişse (writer Dispose
    /// edilmiş, <c>_logs</c> null) en son run dizininden PATH-tabanlı okunur (bkz.
    /// <see cref="RunLogWriter.ReadProjectLogFromDisk"/>) — dizin hâlâ diskte durduğu için sonradan bir proje
    /// kartına tıklamak yine çalışır. <see cref="RunLogWriter"/>'ın KENDİSİ asla dışarı verilmez: host onun
    /// yaşam döngüsüne (ne zaman Dispose edileceğine) müdahale etmemeli. Hiç run koşmadıysa ya da proje o run'da
    /// hiç loglanmadıysa <c>false</c> döner.
    ///
    /// <para>[Task 18] Gerçek disk okuması (<c>File.ReadAllText</c> / <c>ProjectLogFile.Snapshot</c>'ın
    /// FileStream'i) kilit DIŞINDA yapılır: kilit altında yalnız "hangi kaynaktan okunacağı" (canlı writer
    /// referansı ya da en son run dizini) yakalanır. Büyük bir proje logunun okunması artık stopRun/startRun
    /// gibi AYNI kilidi paylaşan ilgisiz çağrıları bloklamaz. Doğruluk korunur: <see cref="RunLogWriter"/> kendi
    /// iç kilitlerini (<c>_projectsGate</c>, <c>ProjectLogFile</c>'ın kendi kilidi) taşır — bu metod dönmeden
    /// SONRA <c>_logs.Dispose()</c> çağrılsa bile (bkz. RunSegmentAsync'in finally'si, dispose HER ZAMAN
    /// worker'lar join olduktan sonra ayrı bir noktada yapılır) yakalanan <see cref="RunLogWriter"/> referansı
    /// burada canlı tutulur (GC toplamaz) ve <c>SnapshotProjectLog</c> kendi kilidiyle Dispose ile serileşir —
    /// canlı-writer anlık görüntüsü hâlâ tutarlıdır.</para>
    /// </summary>
    public bool TryGetProjectLogSnapshot(string projectId, out string text, out int throughLineNumber)
    {
        RunLogWriter? logs;
        string? lastRunDirectory;
        lock (_gate)
        {
            logs = _logs;
            lastRunDirectory = _lastRunDirectory;
        }
        (string Text, int ThroughLineNumber)? snap = logs is not null
            ? logs.SnapshotProjectLog(projectId)
            : lastRunDirectory is not null ? RunLogWriter.ReadProjectLogFromDisk(lastRunDirectory, projectId) : null;
        if (snap is { } s) { text = s.Text; throughLineNumber = s.ThroughLineNumber; return true; }
        text = ""; throughLineNumber = 0; return false;
    }

    /// <summary>
    /// [A6] Run'ı başlatır ve HEMEN döner — run arka planda koşar, aksi halde IPC dispatch loop'u bloklanır
    /// ve <c>stopRun</c> asla ulaşamazdı. Reddedilen istekler (<c>runInProgress</c>/<c>noResumableRun</c>)
    /// dönmeden önce <c>error</c> olayı yazılır.
    /// </summary>
    public async Task StartAsync(StartRunCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        ErrorEvent? rejection = null;
        lock (_gate)
        {
            // [D1 review · A3] ErrorEvent.Message KULLANICIYA ulaşır (şerit "Sync failed — …" / konsol) →
            // uygulama İngilizce-only olduğu için bu metinler de İngilizce.
            if (_disposed)
                rejection = new ErrorEvent("runInProgress", "Supervisor is shutting down — new runs are not accepted.");
            else if (_runActive)
                rejection = new ErrorEvent("runInProgress", $"A run is already in progress — '{cmd.RunId}' was rejected.");
            else if (cmd.Mode == RunMode.Continue && !IsResumableForLocked(cmd.RootPath))
                rejection = new ErrorEvent("noResumableRun", $"No resumable run for '{cmd.RootPath}'.");
            else if (cmd.Mode == RunMode.RetryFailed && !IsRetryableForLocked(cmd.RootPath))
                rejection = new ErrorEvent("noResumableRun", $"No failed projects to retry for '{cmd.RootPath}'.");
            else
            {
                // Slot, arka plan task'ı başlamadan ÖNCE burada tutulur: ikinci bir startRun (planlama sürerken
                // bile) runInProgress alır.
                _runActive = true;
                _finishing = false;
                _stopKind = null;
                _stopAcked = false;
                _runTask = Task.Run(() => ExecuteRunAsync(cmd, ct), CancellationToken.None);
            }
        }
        if (rejection is not null) await writer.WriteAsync(rejection, ct);
    }

    // Continue yalnız AYNI kök için geçerlidir: plan yeniden kurulmaz (T55), bu yüzden başka bir kök için
    // Continue sessizce ESKİ kökün projelerini derlerdi.
    private bool IsResumableForLocked(string rootPath) => HasResumableRunLocked && SameRootLocked(rootPath);

    // [Task-13] RetryFailed de AYNI nedenle (plan yeniden kurulmaz, mevcut plan/logs üstünden devam eder) yalnız
    // AYNI kök için geçerlidir.
    private bool IsRetryableForLocked(string rootPath) => HasRetryableRunLocked && SameRootLocked(rootPath);

    private bool SameRootLocked(string rootPath) =>
        Canonical(rootPath) is string root && string.Equals(root, _root, StringComparison.OrdinalIgnoreCase);

    /// <summary>Bozuk yol (boş/geçersiz karakter) fırlatmaz, null döner: bu, IPC dispatch loop'undan (StartAsync)
    /// çağrılır — hatalı bir komut tüm Supervisor'ı düşürmemeli.</summary>
    private static string? Canonical(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException) { return null; }
    }

    /// <summary>
    /// [I2-K1] Aktif run'ın Stop'unu sahiplenir. <c>true</c> → <c>runStopped</c>'ı (in-flight sonuçları
    /// raporlandıktan SONRA) bu koordinatör yazar. <c>false</c> → sahiplenilecek run yok; çağıran (host) kendi
    /// ack'ini vermelidir.
    ///
    /// <para><b>Graceful:</b> <see cref="ReadySetScheduler.RequestStop"/> — yeni dispatch yok, in-flight
    /// <c>MSBuild.exe</c> child'ları post-build copy DAHİL kendi tamamlanmalarını yapar (ortak çıktı dizini
    /// yarım yazılmış kalmaz); [P3] bu drain penceresi boyunca CPU cap KALDIRILIR (bkz.
    /// <see cref="DrainCapLocked"/>). <b>Hard:</b> inner Job ANINDA terminate edilir; in-flight projeler
    /// <c>projectFailed("stopped")</c> raporlanır. Terminate edilmiş Job yeni process kabul ettiği için ikisi de
    /// Continue'ya açıktır.</para>
    /// </summary>
    public bool TryRequestStop(StopKind kind)
    {
        var warnings = new List<string>(); // [minor 1] konsol yazımı KİLİT DIŞINDA
        bool owned = false;
        lock (_gate)
        {
            if (_runActive && !_finishing)
            {
                owned = true;
                _stopKind = kind == StopKind.Hard ? StopKind.Hard : _stopKind ?? StopKind.Graceful; // Hard geri alınmaz
                if (kind == StopKind.Hard) innerJob.Terminate();
                else if (_stopKind == StopKind.Graceful) DrainCapLocked(warnings); // [P3] Hard'dan SONRA gelen graceful'da anlamsız
                _scheduler?.RequestStop(); // null ise plan hâlâ kuruluyor — kurulur kurulmaz _stopKind okunup uygulanır
                _wake?.WakeAll();          // parked worker'lar IsDone'ı yeniden değerlendirsin
            }
        }
        Warn(warnings);
        return owned;
    }

    /// <summary>
    /// [T20-b/P3 · §3] Graceful drain: cap KALDIRILIR. Graceful stop, in-flight <c>MSBuild.exe</c> child'larının
    /// post-build copy'lerini TAMAMLAMASINA dayanır (ortak çıktı dizininde yarım yazılmış/torn DLL kalmaz) —
    /// o pencereyi %40'lık bir HARD_CAP ile uzatmak, bir doğruluk garantisini zamanlama yarışına çevirirdi.
    /// <para><b>Priority BURADA yazılmaz:</b> priority yalnız öncelik sırasını değiştirir (boşta bir makinede
    /// Idle bir process tam hızda koşar), HARD_CAP ise MUTLAK bir tavandır — drain'i uzatan yarı budur, o yüzden
    /// kaldırılan yalnız cap'tir. [final review I-1] Ama drain KARARI priority'yi de bağlar: bu noktadan sonra
    /// priority taban değerin altına İNDİRİLEMEZ (bkz. <see cref="EffectivePriorityLocked"/>) — aksi halde
    /// drain sürerken gelen bir <c>setPerfMode("Light")</c> in-flight child'ları Idle'a düşürebilirdi.</para>
    /// <para><b>Hard stop yolunda çağrılmaz:</b> orada job zaten <see cref="JobObject.Terminate"/> edilmiştir,
    /// tamamlanacak bir copy yoktur.</para>
    /// <para>Karar run'ın GERİ KALANI için bağlayıcıdır (<see cref="_capDrained"/>): kapanan bir copy-floor
    /// penceresi de, o sırada gelen bir <c>setPerfMode</c> de cap'i geri koyamaz.</para>
    /// </summary>
    private void DrainCapLocked(List<string> warnings)
    {
        // PerfMode taşımayan run'ın job'ına DOKUNULMAZ.
        if (!CapWritableLocked) return;
        // [Fix round 1] KARAR her zaman kaydedilir, YAZIM yalnız gerçekten bir cap varsa yapılır. Bayrağı
        // cap'siz (Full) profilde atlamak, drain sürerken gelen canlı bir setPerfMode("Light")'ın cap:40 yazıp
        // "torn DLL yok" penceresini geri açmasına izin verirdi — kaldırılacak bir şey olmaması, kararın
        // geçersiz olduğu anlamına gelmez.
        _capDrained = true;
        if (_activePerf is { CpuCapPercent: not null }) TryWriteCapLocked(null, warnings);
    }

    /// <summary>
    /// [T20-b/P3 · fix round 1] "Cap'e ŞU AN yazılabilir mi" kuralının TEK yeri. Eskiden aynı kural dört ayrı
    /// noktada (drain · efektif cap · pencere aç/kapa · cap-aktif sorgusu) üç farklı biçimde yazılıydı ve biri
    /// güncellenirken diğerlerinin sessizce ayrışması işten değildi. İki koşul: (1) bu run cap/priority'yi
    /// GERÇEKTEN uygulamış olmalı — PerfMode taşımayan run job'a hiç dokunmaz ve run sınırını geçen bir yazım
    /// sonraki run'ın profilini ezerdi; (2) graceful drain başlamamış olmalı — o karar run'ın geri kalanı için
    /// bağlayıcıdır.
    /// <para>[final review I-1] Priority yarısı da BU predicate'e bağlıdır (<see cref="EffectivePriorityLocked"/>):
    /// kural iki yolda ayrışmasın diye kapı tek yerde durur — orada nötrleme "cap yok" değil "tabandan kötü
    /// değil" biçimindedir.</para>
    /// </summary>
    private bool CapWritableLocked => _perfApplied && !_capDrained;

    /// <summary>
    /// [T20-b/K11] KOŞARKEN perf profilini değiştirir (<c>setPerfMode</c>). <b>Canlı değişen YALNIZ CPU cap +
    /// priority'dir</b>: worker'lar run başında bir kez yaratılır (bkz. <see cref="RunSegmentAsync"/>'in worker
    /// dizisi) ve dinamik bir slot mekanizması YOKTUR — yeni profilin paralelliği ancak BİR SONRAKİ run'da
    /// geçerli olur. App bu ayrımı kullanıcıya konsol notuyla söyler.
    /// <para>[Fix round 1 — KÖK 1] Cap HENÜZ uygulanmamışken (plan hâlâ kuruluyor) gelen niyet KAYBOLMAZ:
    /// <see cref="_pendingPerf"/>'e yazılır ve run başlarken komuttaki <see cref="StartRunCommand.PerfMode"/>'u
    /// ezer. Hiç aktif run yokken NO-OP'tur: kısılacak bir MSBuild child'ı yoktur ve profil zaten bir sonraki
    /// <c>startRun</c> ile gelecektir — aksi halde idle bir Supervisor'ın inner job'ında sahibi olmayan (ve
    /// hiçbir run sonunun geri almayacağı) bir cap kalırdı.</para>
    /// </summary>
    public void ApplyPerfMode(PerfProfile profile)
    {
        var warnings = new List<string>();
        lock (_gate)
        {
            if (!_runActive) return;                                  // sahipsiz cap bırakma
            if (_perfApplied) ApplyPerfLocked(profile, warnings);     // canlı: cap + priority
            else _pendingPerf = profile;                              // planlama penceresi: run başında uygulanacak
        }
        Warn(warnings);
    }

    /// <summary>
    /// [T20-b/K11] Profilin cap + priority yarısını inner job'a yazar ve GERÇEKTEN yürürlükte olan cap'i döner
    /// (yazım başarısızsa <c>null</c> — <c>runStarted</c> ve konsol satırı bu değeri raporlar, istenen değeri
    /// değil).
    /// <para>[Fix round 1 — KÖK 2] İki yazım BAĞIMSIZDIR ve ayrı try/catch'lerdedir: cap yazımı patlarsa
    /// priority yazımı YİNE denenir. Eskiden tek try içindeydiler ve bir cap hatası K11'in priority yarısını
    /// sessizce düşürüyordu (release yolunda daha kötüsü: job Idle'a çakılı kalırdı).</para>
    /// </summary>
    private int? ApplyPerfLocked(PerfProfile profile, List<string> warnings)
    {
        _activePerf = profile; // [P3] copy penceresi kapanınca buraya dönülür
        return WritePerfLocked(profile, warnings);
    }

    /// <summary>
    /// [T20-b/P3 · fix round 1] Bir profili TABAN SÜZGECİNDEN geçirip job'a yazan TEK nokta (run başı + canlı
    /// <c>setPerfMode</c> + copy penceresinin açılışı ve kapanışı aynı buradan geçer). Yazım sırası cap → priority
    /// olarak SABİTTİR ve dönen değer GERÇEKTEN yürürlükte olan cap'tir (yazım patlarsa <c>null</c>):
    /// <c>runStarted</c> ve konsol satırı istenen değeri değil bunu raporlar.
    /// </summary>
    private int? WritePerfLocked(PerfProfile profile, List<string> warnings)
    {
        int? effectiveCap = EffectiveCapLocked(profile.CpuCapPercent);
        bool capWritten = TryWriteCapLocked(effectiveCap, warnings);
        TryWritePriorityLocked(EffectivePriorityLocked(profile.Priority), warnings);
        return capWritten ? effectiveCap : null;
    }

    /// <summary>
    /// [T20-b/P3] Job'a GERÇEKTEN yazılacak cap. İki kural: (1) graceful drain'den sonra cap YOKTUR; (2) açık
    /// bir copy-contention penceresinde taban değerin ALTINA inilmez — canlı bir <c>setPerfMode</c>, sıkışmış
    /// bir post-build copy'yi tam ortasında yeniden kısamaz (o copy zaten MSB3027 sınırına yaklaşmıştır).
    /// Pencere kapalıyken (normal hâl) istenen değeri AYNEN döndürür.
    /// </summary>
    private int? EffectiveCapLocked(int? capPercent)
    {
        if (!CapWritableLocked) return null;
        return _copyFloorDepth > 0 && capPercent is { } cap && cap < PerfProfile.CopyPhaseFloorPercent
            ? PerfProfile.CopyPhaseFloorPercent
            : capPercent;
    }

    /// <summary>
    /// [T20-b/P3 · fix round 1 · final review I-1] <see cref="EffectiveCapLocked"/>'ın priority SİMETRİĞİ:
    /// priority taban değerin (<see cref="PerfProfile.CopyPhaseFloorPriority"/>) ALTINA indirilmez. Tavanı
    /// gevşetip child'ı Idle'da bırakmak floor'un yarısını etkisiz kılardı — yüklü bir makinede Idle bir
    /// process, tavanı serbest olsa bile zamanlayıcıdan sıra alamaz.
    /// <para><b>[final review I-1] Taban İKİ hâlde yürürlüktedir</b> ve kural cap ile AYNI predicate'e
    /// (<see cref="CapWritableLocked"/>) bağlanmıştır — yeni bir dal yok: (1) açık bir copy-contention
    /// penceresi; (2) graceful drain. Drain'in TAMAMI zaten "copy bitsin" penceresidir
    /// (<see cref="DrainCapLocked"/>), dolayısıyla o karardan sonra gelen canlı bir <c>setPerfMode("Light")</c>
    /// in-flight child'ları Idle'a İNDİREMEZ. Eskiden bu kapı yalnız cap tarafında vardı: Stop'a basıldıktan
    /// sonra perf chip'i canlı kaldığı için <c>Full</c>/<c>Balanced</c> koşan bir run drain sırasında Idle'a
    /// düşürülebiliyor, "torn DLL yok" garantisinin yarısı korunmuyordu.</para>
    /// <para>Cap'ten TEK farkı yön: cap için nötr değer "cap yok" (<c>null</c>), priority için "tabandan kötü
    /// değil" — istenen değer zaten tabandan İYİYSE (ör. <c>Full</c>'ün <c>Normal</c>'i) aynen korunur, drain
    /// gereksiz yere yavaşlatılmaz.</para>
    /// <para>Karşılaştırma enum'un SIRASINA dayanır: <see cref="ProcessPriorityClassKind"/> yüksekten alçağa
    /// bildirilmiştir (Normal &lt; BelowNormal &lt; Idle), yani "daha büyük" = "daha düşük öncelik".</para>
    /// </summary>
    private ProcessPriorityClassKind EffectivePriorityLocked(ProcessPriorityClassKind kind) =>
        (!CapWritableLocked || _copyFloorDepth > 0) && kind > PerfProfile.CopyPhaseFloorPriority
            ? PerfProfile.CopyPhaseFloorPriority
            : kind;

    /// <summary>
    /// [T20-b/P3] <see cref="ICopyPhaseCpuFloor.Enter"/>: copy-contention penceresi açar. Cap taban değerin
    /// altındaysa cap DE priority DE tabana çekilir — ve YALNIZ ilk girişte yazılır
    /// (<see cref="_copyFloorDepth"/> ref-count). Cap yoksa (Full / PerfMode'suz run) ya da zaten tabanın
    /// üstündeyse <c>null</c> döner: job'a HİÇ dokunulmaz. Konsol uyarısı kilit DIŞINDA yazılır.
    /// </summary>
    private IDisposable? EnterCopyFloor()
    {
        var warnings = new List<string>();
        bool opened = false;
        lock (_gate)
        {
            if (CapWritableLocked
                && _activePerf is { CpuCapPercent: { } cap } profile && cap < PerfProfile.CopyPhaseFloorPercent)
            {
                opened = true;
                // Sayaç ÖNCE artar: yazımı yapan <see cref="WritePerfLocked"/> aynı profili artık AÇIK pencere
                // kuralıyla (taban süzgeci) değerlendirir — taban değeri burada AYRICA yazılmaz.
                if (_copyFloorDepth++ == 0) WritePerfLocked(profile, warnings);
            }
        }
        Warn(warnings);
        return opened ? new CopyFloorWindow(this) : null;
    }

    /// <summary>
    /// [T20-b/P3] Pencereyi kapatır: cap ve priority ancak SON çıkışta run profiline döner (sayaç sıfırlandığı
    /// için taban süzgeci artık uygulanmaz). Drain sonrası hiçbir şey yazılmaz — cap zaten kalkmıştır ve geri
    /// konmamalıdır; priority'nin tabanda kalması zararsızdır, drain'in tamamı zaten "copy bitsin" penceresidir
    /// ve run sonu geri alma onu Normal'e pinler. <see cref="_perfApplied"/> düşmüşse (run kapanışı) de
    /// yazılmaz: run sınırını geçen bir yazım, sonraki run'ın profilini ezerdi.
    /// </summary>
    private void ExitCopyFloor()
    {
        var warnings = new List<string>();
        lock (_gate)
        {
            if (_copyFloorDepth > 0 && --_copyFloorDepth == 0
                && CapWritableLocked && _activePerf is { } profile)
                WritePerfLocked(profile, warnings);
        }
        Warn(warnings);
    }

    /// <summary>[cycle rounds] Bu run için Stop (graceful ya da hard) istendi mi. SCC tur döngüsünün "yeni tur
    /// açma" kapısı: <see cref="ReadySetScheduler.RequestStop"/> yalnız YENİ DİSPATCH'i keser, halihazırda
    /// in-flight olan bir grubun turlarını kesmez — o karar buradan okunur (bkz. <see cref="ReasonFor"/>, aynı
    /// alanı aynı kilit altında okur).</summary>
    private bool StopRequested
    {
        get { lock (_gate) return _stopKind is not null; }
    }

    /// <summary>[T20-b/P3] Cap-farkındalı backoff'un girdisi: run'da gerçekten bir cap yürürlükte mi. Açık bir
    /// pencerede de <c>true</c>'dur (taban da bir cap'tir), drain'den sonra <c>false</c>.</summary>
    private bool IsCapActiveNow
    {
        get { lock (_gate) return CapWritableLocked && _activePerf is { CpuCapPercent: not null }; }
    }

    /// <summary>
    /// [T20-b/P3] Core'un retry decorator'ına verilen seam. Koordinatörün KENDİSİ bu arayüzü implement etmez:
    /// <c>Enter</c>/<c>IsCapActive</c> public bir yaşam-döngüsü yetkisi değil, tek bir run'ın iç mekanizmasıdır.
    /// </summary>
    private sealed class CoordinatorCpuFloor(RunCoordinator owner) : ICopyPhaseCpuFloor
    {
        public bool IsCapActive => owner.IsCapActiveNow;

        public IDisposable? Enter() => owner.EnterCopyFloor();
    }

    /// <summary>[T20-b/P3] Tek bir copy-contention penceresinin sahibi. Dispose İDEMPOTENT'tir: aynı handle
    /// iki kez kapatılsa bile ref-count TAM BİR KEZ düşer (aksi halde bir çift-kapatma, hâlâ kopyalayan
    /// worker'ın tabanını altından çekerdi).</summary>
    private sealed class CopyFloorWindow(RunCoordinator owner) : IDisposable
    {
        private int _closed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _closed, 1) == 0) owner.ExitCopyFloor();
        }
    }

    // Cap/priority bir OPTİMİZASYONDUR: yazım hatası (ör. rate control desteklenmiyor) run'ı ÖLDÜRMEZ.
    // [Fix round 1 — minor 1] Uyarı BURADA yazılmaz, `warnings`'e biriktirilir: bu metotlar _gate altında
    // çağrılır ve `console` bir I/O kanalıdır — koordinatörün merkezî kilidi I/O boyunca tutulmamalıdır.
    private bool TryWriteCapLocked(int? capPercent, List<string> warnings)
    {
        try { _cpu.ApplyCap(capPercent); return true; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ObjectDisposedException)
        { warnings.Add("warning: cpu cap could not be applied: " + ex.Message); return false; }
    }

    private bool TryWritePriorityLocked(ProcessPriorityClassKind kind, List<string> warnings)
    {
        try { _cpu.ApplyPriority(kind); return true; }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ObjectDisposedException)
        { warnings.Add("warning: cpu priority could not be applied: " + ex.Message); return false; }
    }

    /// <summary>[Fix round 1 — minor 1] Biriktirilmiş uyarıları KİLİT DIŞINDA yazar.</summary>
    private void Warn(List<string> warnings)
    {
        foreach (string line in warnings) console(line);
    }

    /// <summary>
    /// [T20-b/K11] Run bitti → cap ve priority GERİ ALINIR. Zorunludur: inner job Supervisor'ın TÜM ömrü
    /// boyunca yaşar (bkz. <c>Program.Main</c>'deki tek <c>CreateKillOnClose</c>), yani bırakılan bir cap
    /// sonraki run'a ve o job'da doğacak her şeye sessizce miras kalırdı. Normal bitiş, graceful ve hard stop
    /// AYNI yoldan (<see cref="ExecuteRunAsync"/>'in finally'si) geçer.
    /// <para>"Geri alma" = Full profili: cap tamamen kaldırılır, priority Normal'e PİNLENİR. Priority limit
    /// BAYRAĞININ kendisini silen bir API yoktur (bkz. <see cref="JobObject.SetPriorityClass"/>) — ama Normal'e
    /// pinlemek pratikte bayraksız hâlle aynıdır: bayraksızken de child'lar Supervisor'ın (Normal) priority'sini
    /// miras alırdı.</para>
    /// <para>[Fix round 1 — KÖK 2 · Fix round 2 — YENİ 4] Her iki bayrak da KOŞULSUZ temizlenir: bunlar bir
    /// retry defteri değil, RUN'A AİT yaşam-döngüsü işaretleridir ve run sınırını geçmemelidirler. Geri alma
    /// yazımı başarısız olursa sinyal konsol uyarısıdır (<see cref="TryWriteCapLocked"/>/
    /// <see cref="TryWritePriorityLocked"/> onu üretir); bayrağı ayakta tutmak, PerfMode taşımayan bir SONRAKİ
    /// run'ın hiç dokunmadığı job'a run sonunda yazmasına yol açardı — yani "PerfMode'suz run job'a HİÇ
    /// dokunmaz" invaryantını koşullu hale getirirdi.</para>
    /// <para><see cref="_perfApplied"/>'ın burada (geri alma denemesinden hemen sonra) düşmesi ayrıca run'ın
    /// KAPANIŞ PENCERESİNİ güvenli kılar: aşağıdaki <c>await pump</c> sürerken <c>_runActive</c> hâlâ true'dur
    /// ve o aralıkta gelen bir <c>setPerfMode</c> CANLI uygulanabilseydi, geri alacak kimsesi olmayan bir cap
    /// bırakırdı. Bayrak düştüğü için o niyet en fazla <see cref="_pendingPerf"/>'e yazılır; onu da run
    /// kapanışının son kilidi temizler (bkz. <see cref="ExecuteRunAsync"/>).</para>
    /// </summary>
    private void ReleasePerf()
    {
        var warnings = new List<string>();
        lock (_gate)
        {
            _pendingPerf = null;
            // [P3] Copy-floor defteri de run'a AİTTİR ve run sınırını geçmez: bu noktada tüm worker'lar join
            // olmuştur (bkz. ExecuteRunAsync'in finally'si), yani açık kalmış bir pencere olamaz — sıfırlama,
            // beklenmedik bir yol (worker exception'ı) sayacı asılı bıraksa bile sonraki run'ı korur.
            _activePerf = null;
            _copyFloorDepth = 0;
            _capDrained = false;
            if (_perfApplied)
            {
                var full = PerfProfile.For(PerfMode.Full); // cap yok + Normal priority
                TryWriteCapLocked(full.CpuCapPercent, warnings);
                TryWritePriorityLocked(full.Priority, warnings);
                _perfApplied = false;
            }
        }
        Warn(warnings);
    }

    // ---------------------------------------------------------------- run

    private async Task ExecuteRunAsync(StartRunCommand cmd, CancellationToken ct)
    {
        var events = Channel.CreateUnbounded<IpcEvent>(new UnboundedChannelOptions { SingleReader = true });
        var pump = Task.Run(() => PumpEventsAsync(events.Reader, ct), CancellationToken.None);
        try
        {
            await RunSegmentAsync(cmd, events.Writer, ct);
        }
        catch (Exception ex)
        {
            // Beklenmeyen hata: run slotu asla kilitli kalmamalı, App da sessizce beklememeli.
            events.Writer.TryWrite(new ErrorEvent("runFailed", ex.Message));
        }
        finally
        {
            // [T20-b/K11 · Fix round 1 — minor 2] Cap'i EN BAŞTA geri al. Bu finally run'ın TEK huni
            // noktasıdır (erken planFailed/msbuildNotFound dönüşleri ve beklenmeyen exception dahil); aşağıdaki
            // `await pump` beklenmedik bir şekilde fırlarsa bile uygulanmış bir cap Supervisor ömrü boyunca
            // SIZMAZ. Bu noktada tüm worker'lar zaten join olmuştur (RunSegmentAsync döndü), yani kısılacak bir
            // MSBuild child'ı kalmamıştır.
            ReleasePerf();
            // ACK BORCU: TryRequestStop true dediyse runStopped'ı yazmak BİZİM sorumluluğumuzdur — ama run,
            // runStarted'a hiç ulaşmamış olabilir (planFailed/msbuildNotFound ya da beklenmeyen bir hata; ör.
            // kullanıcı 177 projelik bir planlama sürerken Stop'a bastı). O yolda aşağıdaki finally çalışmadığı
            // için ack burada kapatılır; aksi halde App sonsuza dek runStopped bekler.
            StopKind? unacked;
            lock (_gate)
            {
                unacked = _stopKind is not null && !_stopAcked ? _stopKind : null;
                _stopAcked = true;
            }
            if (unacked is not null)
                events.Writer.TryWrite(new RunStoppedEvent(cmd.RunId, WasHard: unacked == StopKind.Hard));

            events.Writer.Complete();
            await pump; // tüm event'ler yazıldıktan SONRA run task'ı biter
            lock (_gate)
            {
                _runActive = false;
                _finishing = false;
                _stopKind = null;
                _scheduler = null;
                _wake = null;
                // [Fix round 2 — YENİ 1/4] Perf state'i de BURADA, `_runActive = false` ile AYNI kritik
                // bölgede sıfırlanır. Yukarıdaki ReleasePerf() ile arada `await pump` vardır ve o aralıkta
                // `_runActive` hâlâ true'dur: IPC dispatch loop'u ayrı thread'dedir, setPerfMode'un hiçbir
                // run-state ön koşulu yoktur (bkz. SupervisorHost'un dispatch'i) ve o pencerede gelen bir
                // niyet _pendingPerf'e düşerdi — temizleyeni olmadığı için BİR SONRAKİ run'ın profilini
                // sessizce ezerdi (cap'siz olması gereken bir run capli başlardı).
                _pendingPerf = null;
                _perfApplied = false;
                // [P3] ReleasePerf ile AYNI defter; o çağrı ile bu kilit arasındaki `await pump` penceresinde
                // (_runActive hâlâ true) yeni bir pencere açılamaz — _perfApplied düştüğü için Enter NO-OP'tur.
                _activePerf = null;
                _copyFloorDepth = 0;
                _capDrained = false;
            }
        }
    }

    /// <summary>Tek FIFO kanal → tek yazıcı: event SIRASI korunur, çağıran thread'ler bloklanmaz.</summary>
    private async Task PumpEventsAsync(ChannelReader<IpcEvent> reader, CancellationToken ct)
    {
        bool broken = false;
        await foreach (var ev in reader.ReadAllAsync(CancellationToken.None))
        {
            if (broken) continue; // kanal yine de sonuna kadar tüketilir (yazıcı asla bloklanmaz)
            try { await writer.WriteAsync(ev, ct); }
            catch (IpcFramingException) { /* tek mesaj çok büyük — yalnız O atlanır, akış bozulmaz */ }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            { broken = true; } // App/stdout gitti — run devam eder, disk logu tek gerçek kaynaktır [D4]
        }
    }

    private async Task RunSegmentAsync(StartRunCommand cmd, ChannelWriter<IpcEvent> events, CancellationToken ct)
    {
        RunPlan runPlan;
        RunLogWriter logs;
        RunClock clock;
        // [cycle rounds] Scheduler'ın TOHUMU: resume'da devralınan snapshot, Build'de "up to date" pre-skip
        // tohumu, aksi halde null (taze, tohumsuz). Scheduler'ın KENDİSİ aşağıda, dalların DIŞINDA tek bir
        // yerde kurulur — üç ayrı kurulum, üçünde de aynı CycleGroups'u vermeyi unutmaya açıktı (kopya YASAK).
        RunSnapshot? schedulerSeed = null;
        long elapsedAtStart;
        ConcurrentDictionary<string, IReadOnlyList<string>> depIssuesById;
        ConcurrentDictionary<string, byte> stoppedFailedIds;
        // [I2-K2/Task 10 · A4] Bu segmentin obj kökü: taze run'da resolver'dan hesaplanır ve run state'ine
        // yazılır, resume yolunda AYNI run'ın saklanan kökünden okunur (bkz. _worktreeObjRoot).
        string? worktreeObjRoot;
        // [Task 19] Build modunda incremental olarak "up to date" (WillBuild==false, cycle DIŞI) pre-skip edilen
        // projeler — cycle pre-skip'i gibi construction anında Skipped sayılır (dependent'ları için resolved),
        // ProjectSkippedEvent("skipped — up to date") ile raporlanır. Rebuild/Continue/RetryFailed'de boş kalır.
        // [cycle rounds/Task 8] CycleUnconverged BURADA (tipli üçüncü alan) taşınır — bu listeye HEM sıradan
        // güncel skip'ler HEM de yakınsamama hafızasından gelen SCC pre-skip'leri düşer; App'e giden ayırt
        // edici bayrak Reason METNİNDEN çıkarılmaz (kopya YASAK), doğrudan bu tuple alanından DecideSkipped'e taşınır.
        var upToDateSkips = new List<(string ProjectId, string Reason, bool CycleUnconverged)>();

        if (cmd.Mode is RunMode.Continue or RunMode.RetryFailed)
        {
            RunSnapshot snapshot;
            // [T54] 1. segmentin depIssue birikimi AYNEN devralınır — yoksa (savunmacı) taze başlar. [Task-13]
            // stoppedFailedIds birikimi de AYNI şekilde devralınır (Continue'un reason=stopped tespiti için).
            lock (_gate)
            {
                runPlan = _plan!; logs = _logs!; snapshot = _snapshot!;
                depIssuesById = _depIssuesById ??= new(StringComparer.OrdinalIgnoreCase);
                stoppedFailedIds = _stoppedFailedIds ??= new(StringComparer.OrdinalIgnoreCase);
                worktreeObjRoot = _worktreeObjRoot; // [A4] resume: cmd'nin bayrakları DEĞİL, orijinal run'ın kökü
            }
            // [T55] AYNI plan'dan devam: yeniden tarama/planlama YOK. Snapshot.Queued INERT'tir — resume ctor'u
            // kuyruğu Completed'tan türetir; RetryPlanning ise Completed'ı dispatch edilecek yeni bir kümeyle
            // (bkz. iki mod ayrımı aşağıda) buluşturarak resume ctor'a besler.
            //
            // [Task-13] Continue: yalnız reason="stopped" ile Failed'a düşenler (torn-DLL guard) Queued'a döner —
            // diğer reason'larla Failed olanlar (ör. "exit 1") Failed kalır, yeniden derlenmez.
            // RetryFailed: Failed olan HER proje + transitive dependent'ları Queued'a döner; succeeded/skipped
            // dokunulmaz. İkisinde de elapsed/console/log writer SIFIRLANMAZ — aynı segment üstünden devam.
            RunSnapshot effectiveSnapshot = cmd.Mode == RunMode.Continue
                ? RetryPlanning.RequeueStoppedFailed(snapshot, new HashSet<string>(stoppedFailedIds.Keys, StringComparer.OrdinalIgnoreCase))
                : RetryPlanning.RequeueFailedAndDependents(runPlan.Plan, snapshot);
            schedulerSeed = effectiveSnapshot;
            elapsedAtStart = snapshot.ElapsedMs;
            clock = new RunClock(nowMs, snapshot.ElapsedMs);
        }
        else
        {
            // [Fix wave 1 — Finding 3] WorktreePreparationException: planner (Program.BuildRunPlan) seçili
            // branch AKTİF branch'ten farklıyken worktree'yi hazırlayamadı. Worktree o durumda zorunludur
            // (K1) — in-place'e düşmek YANLIŞ branch'i derlemek olurdu, bu yüzden run HİÇ BAŞLAMAZ. Ayrı bir
            // kanal AÇILMAZ: mevcut planlama-hatası kodu (planFailed) kullanılır — App'in RunEndingErrorCodes
            // kümesi onu zaten tanır (mesaj kullanıcıya gösterilir, Build butonu geri açılır).
            // [planlama görünürlüğü] Planlayıcının adım satırları AYNI FIFO kanaldan gider: sıra korunur,
            // yani hepsi aşağıdaki runStarted'dan ÖNCE App'e ulaşır. TryWrite unbounded kanalda hiç bloklamaz —
            // planlayıcı senkron çalıştığı için bu şarttır.
            try { runPlan = planner(cmd, line => events.TryWrite(new PlanProgressEvent(line))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or WorktreePreparationException)
            { events.TryWrite(new ErrorEvent("planFailed", ex.Message)); return; }

            // [I2-K2/Task 10 · A4] cmd.UseWorktree=false → HER ZAMAN null (in-place, VS-parity). true iken
            // resolver YOKSA (ör. testlerin basit harness'ı) yine null'a düşer — obj izolasyonu ancak resolver
            // GERÇEK bir worktree kökü döndürdüğünde devreye girer. Planner'dan SONRA çağrılır: Program.cs'te
            // worktree'yi HAZIRLAYAN taraf planner'dır, resolver yalnız onun çözdüğü kökü okur.
            worktreeObjRoot = cmd.UseWorktree ? worktreeObjRootResolver?.Invoke(cmd) : null;

            lock (_gate)
            {
                _logs?.Dispose(); // terk edilmiş (artık sürdürülmeyecek) önceki run'ın writer'ı
                _snapshot = null;
                _plan = runPlan;
                _root = Canonical(cmd.RootPath);
                _worktreeObjRoot = worktreeObjRoot; // [A4] Continue/RetryFailed segmentleri bunu miras alacak
                logs = _logs = logFactory(DateTimeOffset.Now);
                _lastRunDirectory = logs.RunDirectory;
                depIssuesById = _depIssuesById = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase); // [T54] taze run → taze birikim
                stoppedFailedIds = _stoppedFailedIds = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase); // [Task-13] taze run → taze birikim
            }
            // [Task 19] Build modunda: planlayıcının hesapladığı WillBuild==false projeler "up to date" olarak
            // pre-skip edilir — scheduler'a Skipped tohumlanır (dependent'ları için resolved), dispatch
            // edilmezler. Rebuild HER ŞEYİ derler (tohum yok).
            //
            // [cycles] Cycles modunda KAPSAM daralır: iş yalnız SCC'ler VE onların transitif upstream'idir
            // (gerekçe CycleRunScope'ta) — kapsam dışı her proje koşulsuz pre-skip edilir. Kapsam İÇİNDEKİLER
            // sıradan incremental kurala tabidir; SCC'ler ayrıca iki kapıdan geçebilir — daha önce aynı bileşik
            // imzada yakınsamamış olmak, ya da grup olarak güncel olmak.
            bool cyclesRun = cmd.Mode == RunMode.Cycles;
            var cycleScope = cyclesRun ? CycleRunScope.Of(runPlan.Plan) : null;
            if (cmd.Mode == RunMode.Build || cyclesRun)
            {
                var seed = new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase);
                // Grup düzeyinde "güncel" bulunan SCC üyeleri — aşağıdaki tek pre-skip döngüsünün cycle
                // üyelerine açtığı KAPIDIR. Build modunda boş kalır (kapı kapalı: Build bir SCC'yi derlemez ve
                // onları tohumlamak ReadySetScheduler'ın "in dependency cycle" gerekçesini YUTARDI).
                var cycleUpToDate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (cyclesRun)
                {
                    // [Task 7] Daha önce yakınsAMAMIŞ bir SCC, bileşik imzası HÂLÂ o yakınsamama anındakiyle
                    // eşleşiyorsa bir daha tur harcamaz: TÜM üyeleri, up-to-date skip'iyle AYNI mekanizmayla
                    // (seed + aşağıdaki DecideSkipped döngüsü) pre-skip edilir — grup hiç dispatch edilmeden.
                    // Kaynak değiştiyse (imza farklı) bu döngü hiçbir şey eklemez, grup YENİDEN denenir. Yalnız
                    // TÜM üyeler eşzamanlı hafızalıysa (All) pre-skip edilir — savunmacı: SCC üyeliği plan
                    // değişince genişleyebilir/daralabilir.
                    if (stateStore is not null && runPlan.Incremental is { } inc)
                    {
                        var cycleState = stateStore.Load();
                        foreach (var cycle in runPlan.Plan.Cycles)
                        {
                            // [I4] Temsilci seçimi YAZAN tarafla (UpdateCycleNonConvergenceMemory) TEK yerdedir:
                            // bu liste ordinal, oradaki build-order sıralıdır — kendi [0]'larını seçselerdi
                            // üye-başına imzanın ayrıştığı modda (Fast) iki taraf farklı imzaya bakardı.
                            if (CycleGroups.SignatureRepresentative(cycle) is not { } representative
                                || !inc.SignatureById.TryGetValue(representative, out var signature)) continue;
                            if (!cycle.All(id => BuildStateStore.IsCycleNonConvergent(cycleState, id, signature))) continue;
                            foreach (string id in cycle)
                            {
                                seed[id] = BuildResult.Skipped;
                                upToDateSkips.Add((id, SkipReasons.CycleNonConvergent, CycleUnconverged: true));
                            }
                        }
                    }
                    // SCC'ler de incremental olur: Cycles modunda planlayıcı üyelere GERÇEK bir WillBuild verir
                    // (bileşik imza — tüm üyeler için ORTAK), dolayısıyla "hepsi false" ⇒ grup gerçekten güncel
                    // demektir ve yeniden derlenmemelidir. Karar GRUP düzeyindedir (All): kısmi bir durumda
                    // (ör. bir üyenin state'i hiç yok) hiçbir üye tohumlanmaz — yarısı Skipped tohumlanmış bir
                    // grubu dispatch etmek bozuk olurdu.
                    var willBuildById = runPlan.Plan.Nodes.ToDictionary(
                        n => n.Id, n => n.WillBuild, StringComparer.OrdinalIgnoreCase);
                    foreach (var cycle in runPlan.Plan.Cycles)
                    {
                        if (cycle.Count == 0) continue;
                        if (!cycle.All(id => willBuildById.TryGetValue(id, out bool? wb) && wb == false)) continue;
                        foreach (string id in cycle) cycleUpToDate.Add(id);
                    }
                }
                foreach (var n in runPlan.Plan.Nodes)
                {
                    if (seed.ContainsKey(n.Id)) continue;   // yakınsamama hafızası zaten karar verdi
                    // KAPSAM kapısı — iki modda AYRI ve bilerek öyle:
                    // · Cycles: kapsam dışı kalan BURADA tohumlanır ve kendi gerekçesiyle raporlanır.
                    // · Build:  SCC üyesi HİÇ tohumlanmaz, çünkü onu zaten ReadySetScheduler kendi
                    //   "in dependency cycle" gerekçesiyle pre-skip eder — tohumlamak o gerekçeyi yutar ve
                    //   iki farklı durum ekranda aynı görünürdü.
                    if (cyclesRun && !cycleScope!.Contains(n.Id))
                    {
                        seed[n.Id] = BuildResult.Skipped;
                        upToDateSkips.Add((n.Id, SkipReasons.OutOfCycleScope, CycleUnconverged: false));
                        continue;
                    }
                    if (!cyclesRun && n.InCycle) continue;
                    // Cycle üyesi buraya YALNIZ grup kapısından geçtiyse gelir: tekil WillBuild bir SCC üyesini
                    // TEK BAŞINA temsil etmez (bileşik imza gruba aittir). Kapsamdaki upstream sıradan projedir
                    // ve bu kapıya hiç uğramaz — kendi WillBuild'i onu temsil eder.
                    if (n.InCycle && !cycleUpToDate.Contains(n.Id)) continue;
                    if (n.WillBuild != false) continue;
                    seed[n.Id] = BuildResult.Skipped;
                    upToDateSkips.Add((n.Id, SkipReasons.UpToDate, CycleUnconverged: false));
                }
                if (seed.Count > 0) schedulerSeed = new RunSnapshot(seed, [], 0);
            }
            elapsedAtStart = 0;
            clock = new RunClock(nowMs);
        }

        // [cycle rounds] SCC üyelik haritası TEK yerde kurulur ve HEM scheduler'a (grup dispatch'i) HEM run
        // context'ine (tur döngüsü) AYNI örnek verilir — ikisi ayrı From çağrısıyla kurulsaydı üye sırası
        // sessizce ayrışabilirdi. Plan'da hiç SCC yoksa null geçilir: scheduler o zaman bugünkü davranışını
        // (InCycle düğümleri pre-skip) birebir korur ve tur döngüsü hiç devreye girmez.
        // Kapsam kapısı BURADADIR: Cycles DIŞINDAKİ her modda harita hiç KURULMAZ ve scheduler'a null gider —
        // yani diğer modlar için yazılmış ayrı bir kod yolu yoktur, "SCC yok" hâliyle BİREBİR aynı dal seçilir.
        var groups = cmd.Mode == RunMode.Cycles && CycleGroups.From(runPlan.Plan) is { Count: > 0 } withCycles
            ? withCycles
            : null;
        var scheduler = schedulerSeed is null
            ? new ReadySetScheduler(runPlan.Plan, groups)
            : new ReadySetScheduler(runPlan.Plan, schedulerSeed, groups);

        MsBuildToolset toolset;
        try { toolset = await msbuildFactory(ct); }
        catch (MsBuildResolveException ex)
        { events.TryWrite(new ErrorEvent("msbuildNotFound", ex.Message)); return; }

        // [T72/Task 14] SPIKE S2 — bayat-obj (yabancı-TFM restore artığı) teşhisi YALNIZ taze (Rebuild/Build)
        // segmentte VE in-place (worktreeObjRoot null — izole obj YOK) projeler için tetiklenir: Continue/RetryFailed
        // AYNI obj üstünde devam eder (yeniden teşhis gerekmez), worktree run'ları zaten PAYLAŞILMAYAN izole obj
        // kullanır (bayat-obj zehri worktree'de oluşamaz). onRetry ile AYNI ikili-yazım deseni: hem decision.log
        // hem konsol. Dokunmaz, yalnız warn (StaleObjRunStartWarner ASLA fırlatmaz).
        if (cmd.Mode is not (RunMode.Continue or RunMode.RetryFailed) && worktreeObjRoot is null)
            StaleObjRunStartWarner.WarnStaleObj(runPlan.Plan.Nodes, line => { Decide(logs, line); console(line); });

        int parallelism = Math.Max(1, cmd.Parallelism);
        // [T20-b/K11] Perf profili: PARALELLİK BURADAN GELMEZ (o, komutun kendi alanıdır — App aynı tablodan
        // türetir ve Continue/RetryFailed segmentleri de onu taşır). Buradan yalnız CPU cap + priority alınır.
        // PerfMode yoksa ya da çözülemiyorsa profil null'dır ve job'a HİÇ dokunulmaz (geriye dönük uyum).
        PerfProfile? perf = cmd.PerfMode is { } perfModeText ? PerfProfile.TryParse(perfModeText) : null;
        int? appliedCap = null;                 // GERÇEKTEN yürürlükte olan cap (runStarted + konsol bunu yazar)
        var perfWarnings = new List<string>();  // [minor 1] kilit dışında yazılır
        var wake = new WakeSignal();
        lock (_gate)
        {
            _scheduler = scheduler;
            _wake = wake;
            // [Fix round 1 — KÖK 1] Planlama penceresinde gelmiş bir setPerfMode komuttakini EZER: o,
            // kullanıcının DAHA SONRAKİ sözüdür. Niyet burada TÜKETİLİR (bir sonraki run'a taşınmaz).
            perf = _pendingPerf ?? perf;
            _pendingPerf = null;
            if (perf is { } profile) { _perfApplied = true; appliedCap = ApplyPerfLocked(profile, perfWarnings); }
            if (_stopKind is not null) scheduler.RequestStop(); // plan kurulurken gelmiş Stop
        }
        Warn(perfWarnings);

        clock.Start();
        var plan = runPlan.Plan;
        var nodeById = plan.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        events.TryWrite(new RunStartedEvent(cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism,
            plan.Configuration, elapsedAtStart, appliedCap));
        // [Task 17] runStarted'dan HEMEN SONRA, ilk projectStarted/projectSkipped'ten ÖNCE: App'in Projects
        // listesini will-build önizlemesiyle pre-populate edebilmesi için. WillBuild alanı doğrudan plan'ın
        // düğümlerinden (BuildPreview/IncrementalPlanner'ın doldurduğu — henüz run akışına tam bağlanmadıysa null)
        // taşınır; burada AYRICA hesaplanmaz.
        // [W1] BuiltCommit (sha çiftinin sol yarısı) da BURADAN taşınır — Sync'te doldurup burada boş bırakmak,
        // run başlar başlamaz kartların sha slotunu sıfırlardı. Load() ITEM BAŞINA DEĞİL, TOPLU okunur —
        // önizlemenin tamamı tek okumadan beslenir. (Bir Build segmenti store'u toplam İKİ kez okur: yukarıdaki
        // [Task 7] yakınsamama hafızası taraması ve buradaki önizleme; ikisi ayrı sorulardır ve Build DIŞINDAKİ
        // modlarda ilki hiç çalışmaz.) Segment 2'nin okuduğu
        // map segment 1'in persist'lerini İÇERİR — yani derlenmiş satırların sol yarısı taze commit'e döner.
        var builtCommits = stateStore?.Load();
        // Önizleme BU KOŞUNUN yapacağını anlatır, planlayıcının soyut "dirty mi" cevabını değil: pre-skip
        // edilmiş her proje WillBuild=false gösterilir. İki yer arasındaki fark aksi halde kullanıcıya YALAN
        // söylerdi — amber "derlenecek" noktası, hemen ardından "skipped" olarak geçen bir satırda. Build
        // modunda bu projeksiyon çoğunlukla no-op'tur (tohum zaten WillBuild==false'tan doğar); ayrıştığı iki
        // yer, gerekçesi imzadan DEĞİL koşu-zamanlama kuralından gelen skip'lerdir: yakınsamama hafızası ve
        // Cycles modunun kapsam dışı bıraktığı projeler.
        var preSkipped = upToDateSkips.Select(s => s.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        events.TryWrite(new BuildPreviewEvent(
            [.. plan.Nodes.Select(n => new BuildPreviewItem(n.Id, n.Name,
                preSkipped.Contains(n.Id) ? false : n.WillBuild,
                BuildStateStore.BuiltCommitOf(builtCommits, n.Id)))]));
        // [A1/T15] Katman ataması ters-katman bağımlılığı bulduysa (warn-only DATA — koordinatör bunları
        // okuyup bloklama/yeniden sıralama YAPMAZ) run başında konsola basılır: LayerEngine'ın ürettiği metin
        // AYNEN, yalnız "warning: " öneki eklenerek. Uyarı kullanıcıya ulaşmazsa, bariyerin bir projeyi kendi
        // bağımlılığından önce koyduğu plan sessizce derlenirdi — tek gerçek düzeltme pattern'leri gözden
        // geçirmektir. Plan katmansızsa (varsayılan) LayerWarnings null/boştur → hiçbir satır basılmaz.
        foreach (string warning in plan.LayerWarnings ?? [])
            console("warning: " + warning);
        // v7Δ-7: konsolda solution-level msbuild izlenimi verilmez — motorun gerçeği proje-başına shell-out'tur,
        // gerçek komut satırları proje loglarındadır.
        console(string.Format(CultureInfo.InvariantCulture,
            // [D1 review · A3] console(...) satırları KULLANICININ konsolunda görünür → İngilizce.
            "Run {0} ({1}): {2} projects, {3} workers, {4}, {5} — each project is built as its own compiler child process; command lines are in the project logs.",
            cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism, plan.Configuration,
            perf is null ? PerfNoteText.CapTextUnset : PerfNoteText.CapText(appliedCap)));
        Decide(logs, string.Format(CultureInfo.InvariantCulture,
            "run {0} started: mode={1} projects={2} parallelism={3} configuration={4} cpuCap={5} elapsedAtStart={6}ms",
            cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism, plan.Configuration,
            perf is null ? PerfNoteText.CapValueUnset : PerfNoteText.CapValue(appliedCap), elapsedAtStart));

        // runStarted yazıldı: buradan SONRA hangi yoldan çıkılırsa çıkılsın (beklenmeyen exception dahil)
        // kapanış olayları TAM OLARAK BİR KEZ yazılır — aksi halde App'in run'ı sonsuza dek "koşuyor" kalırdı.
        try
        {
            // [A13/B2] Skip satırının metni TEK yerde: iki döngü de aynı cümleyi kuruyordu, çeviri sonrası
            // birebir aynı literal iki kez yaşardı (kopya YASAK, CLAUDE.md).
            // [cycle rounds/Task 8] cycleUnconverged TİPLİ bir parametredir, Reason'dan ÇIKARILMAZ — çağıranın
            // hangi listeden geldiğini (yakınsamama hafızası mı, sıradan güncel skip mi) zaten bildiği için
            // burada yalnız ProjectSkippedEvent'e AYNEN taşınır.
            void DecideSkipped(string projectId, string reason, bool cycleUnconverged = false)
            {
                events.TryWrite(new ProjectSkippedEvent(cmd.RunId, projectId, reason, cycleUnconverged));
                Decide(logs, $"{nodeById[projectId].Name}: skipped — {reason}");
            }

            // Cycle üyeleri (construction anında Skipped) — resume edilmiş scheduler'ın PreSkipped'i BOŞTUR,
            // bu yüzden Continue'da tekrar yazılmazlar (yalnız snapshot onları taşımıyorsa savunmacı olarak yazılır).
            // Bu liste yalnız "grup DIŞARIDA hiç dispatch edilmedi" pre-skip'ini taşır (kill switch/SCC yok) —
            // yakınsamama hafızasıyla İLGİSİZDİR, cycleUnconverged varsayılan false kalır.
            foreach (var (projectId, reason) in scheduler.PreSkipped)
                DecideSkipped(projectId, reason);
            // [Task 19] Build modunda incremental "up to date" skip'ler (cycle pre-skip ile AYNI konumda, ilk
            // dispatch'ten ÖNCE): dependent'ları için scheduler'da zaten Skipped/resolved tohumlandı.
            foreach (var (projectId, reason, cycleUnconverged) in upToDateSkips)
                DecideSkipped(projectId, reason, cycleUnconverged);

            var run = new RunContext(
                cmd.RunId, plan.Configuration, runPlan.SolutionRefs, nodeById,
                scheduler, wake, logs, events,
                // Retry politikası Core'un [T8]; burada yalnız run'a bağlanır: onRetry hem decision.log'a hem konsola.
                // [T20-b/P3] cpuFloor: contention penceresinde cap'i tabana yükseltir — cap'in TEK yazıcısı
                // bu koordinatör olduğu için seam de buraya bağlanır (bkz. EnterCopyFloor/ExitCopyFloor).
                new RetryingMsBuildInvoker(toolset.Invoker, RetryingMsBuildInvoker.DefaultBackoff,
                    retryDelay ?? ((delay, token) => Task.Delay(delay, token)),
                    onRetry: message => { Decide(logs, message); console(message); },
                    cpuFloor: new CoordinatorCpuFloor(this)),
                toolset.MsBuildExePath,
                worktreeObjRoot,
                depIssuesById, // [T54]
                stoppedFailedIds, // [Task-13]
                stateStore, // [Task 19] projectSucceeded → BuildState persist (null ⇒ persist YOK, mevcut test davranışı)
                runPlan.Incremental, // [Task 19] imza + HEAD + branch (persist için)
                groups); // [cycle rounds] scheduler ile AYNI örnek — dispatch edilen id bir grup üyesi mi

            var workers = Enumerable.Range(0, parallelism)
                .Select(_ => Task.Run(() => WorkerAsync(run, ct), CancellationToken.None))
                .ToArray();
            try { await Task.WhenAll(workers); }
            catch (Exception ex)
            {
                // Worker'lar normalde fırlatmaz (her proje kendi sonucunu raporlar). Yine de fırlarsa: run ASILI
                // KALMAZ — aşağıdaki finally snapshot alıp runCompleted yazar; kalanlar Queued olarak raporlanır.
                Decide(logs, "a worker terminated unexpectedly: " + ex.Message);
            }
        }
        finally
        {
            // [Kısıt 4] Snapshot ANCAK tüm worker'lar join olduktan sonra alınır (her in-flight proje sonucunu
            // raporlamıştır) — hem graceful hem hard için. Böylece Queued kesindir, "öldürüldü" ≠ "raporlandı"
            // belirsizliği yoktur.
            clock.Pause();
            var snapshotAtEnd = scheduler.TakeSnapshot(clock.ElapsedMs);

            StopKind? stopKind;
            // _finishing: bundan sonra TryRequestStop sahiplenmez. _stopAcked: runStopped'ı BURADA yazıyoruz,
            // ExecuteRunAsync'in ack-borcu kapatıcısı bir daha yazmasın (tek runStopped garantisi).
            lock (_gate) { stopKind = _stopKind; _finishing = true; if (stopKind is not null) _stopAcked = true; }

            var outcome = stopKind is null ? RunOutcome.Completed : RunOutcome.Stopped;
            var completed = scheduler.Completed;
            int succeeded = completed.Count(kv => kv.Value == BuildResult.Succeeded);
            int failed = completed.Count(kv => kv.Value == BuildResult.Failed);
            int skipped = completed.Count(kv => kv.Value == BuildResult.Skipped);
            // [T54] Run genelinde (Continue segmentleri DAHİL, kümülatif) dependency-affected proje sayısı —
            // depIssues'u boş OLMAYAN projeler. Kendisi failed bir kök, kendi depIssue'unu taşımaz (sayılmaz).
            int depIssueCount = depIssuesById.Values.Count(v => v.Count > 0);

            // Olaylar ÖNCE (TryWrite fırlatmaz), disk logu sonra: log I/O'su patlasa bile App kapanışı görür.
            if (stopKind is not null)
                events.TryWrite(new RunStoppedEvent(cmd.RunId, WasHard: stopKind == StopKind.Hard));
            events.TryWrite(new RunCompletedEvent(cmd.RunId, outcome, succeeded, failed, skipped,
                snapshotAtEnd.Queued.Count, clock.ElapsedMs, depIssueCount));
            Decide(logs, string.Format(CultureInfo.InvariantCulture,
                "run {0} finished: outcome={1} succeeded={2} failed={3} skipped={4} queued={5} duration={6}ms depIssues={7}",
                cmd.RunId, outcome, succeeded, failed, skipped, snapshotAtEnd.Queued.Count, clock.ElapsedMs, depIssueCount));

            // [Task-13] Continue backlog'u (Stopped + Queued>0) VEYA en az bir Failed proje varsa (RetryFailed'a
            // açık — bkz. HasRetryableRunLocked) plan/logs/birikimler devredilir; ikisi de yoksa (run tamamen
            // temiz bitti) her şey temizlenir — aksi halde RetryFailed'ın "yeniden derlenecek" bir kümesi olmaz.
            bool resumable = (outcome == RunOutcome.Stopped && snapshotAtEnd.Queued.Count > 0) || failed > 0;
            lock (_gate) _snapshot = resumable ? snapshotAtEnd : null;
            if (!resumable)
            {
                // [Kısıt 1] RunLogWriter ancak TÜM worker'lar join olduktan sonra dispose edilir.
                // [A4] _worktreeObjRoot da temizlenir: devredilecek bir run kalmadığında bu kökün yaşaması
                // için sebep yoktur (taze run onu zaten her defasında yeniden yazar — sızma değil, hijyen).
                lock (_gate) { _logs = null; _plan = null; _root = null; _depIssuesById = null; _stoppedFailedIds = null; _worktreeObjRoot = null; } // [T54/Task-13/A4]
                logs.Dispose();
            }
        }
    }

    /// <summary>
    /// decision.log'a yazar. Log bir TANI kaydıdır: disk hatası (dolu disk vb.) run'ı ÖLDÜRMEMELİ — konsola uyarı
    /// düşer ve run devam eder. <see cref="ObjectDisposedException"/> KASITLI olarak yakalanmaz: o, bu sınıfın
    /// kendi log yaşam-döngüsü hatası demektir (kısıt 1) ve sessizce yutulmamalıdır.
    /// </summary>
    private void Decide(RunLogWriter logs, string line)
    {
        try { logs.AppendDecision(line); }
        catch (IOException ex) { console("warning: decision.log could not be written: " + ex.Message); }
    }

    // ---------------------------------------------------------------- worker

    private async Task WorkerAsync(RunContext run, CancellationToken ct)
    {
        while (true)
        {
            // Beklenecek sinyal, KOŞUL KONTROLÜNDEN ÖNCE yakalanır: kontrol ile park arasında gelen bir uyandırma
            // kaçırılmaz (lost wakeup yok).
            var wake = run.Wake.Waiter;
            if (run.Scheduler.IsDone) return;
            if (!run.Scheduler.TryDispatch(out string projectId))
            {
                // [Kısıt 2] TryDispatch==false "run bitti" DEĞİL, "şu an hazır iş yok" demektir — bağımlılıklar
                // hâlâ derleniyor olabilir. Burada dönmek run'ı sessizce kırpardı; bunun yerine park edilir.
                try { await wake.WaitAsync(ct); }
                catch (OperationCanceledException) { return; } // Supervisor kapanıyor
                continue;
            }
            // [cycle rounds] Dispatch edilen id bir SCC üyesiyse scheduler TÜM üyeleri in-flight işaretlemiştir
            // (bkz. ReadySetScheduler.TryDispatch) — o zaman iş kalemi tek bir proje değil, grubun TAMAMIDIR.
            var members = run.Groups?.MembersOf(projectId) ?? [];
            try
            {
                if (members.Count == 0) await BuildProjectAsync(run, projectId, ct);
                else await BuildCycleGroupAsync(run, members, ct);
            }
            finally { run.Wake.WakeAll(); } // Complete edildi (ya da patladı) → parked worker'lar yeniden baksın
        }
    }

    /// <summary>[cycle rounds] Tek bir invoke'un sonucu — <see cref="InvokeOnceAsync"/> ile çağıranı ayıran sözleşme.</summary>
    private sealed record InvokeOutcome(BuildResult Result, long DurationMs, string? FailReason);

    /// <summary>Tek projenin tüm yaşam döngüsü. <see cref="ReadySetScheduler.Complete"/> her yoldan TAM BİR KEZ çağrılır.</summary>
    private async Task BuildProjectAsync(RunContext run, string projectId, CancellationToken ct)
    {
        // Dispatch ile Complete arasındaki HER ŞEY try/finally içinde: buradan fırlayan bir exception Complete'i
        // atlarsa proje sonsuza dek in-flight kalır (IsDone asla true olmaz) ve run ASILIR. Sonuç bu yüzden TEK
        // bir yerden — finally'deki ReportProjectResult'tan — raporlanır; üç yol (normal / iptal / beklenmeyen
        // hata) yalnız aşağıdaki yerel değişkenleri doldurur.
        var result = BuildResult.Failed;
        long durationMs = 0;
        string? failReason = null;
        string? failLogTail = null;

        // [T54] depIssues invoke'tan ÖNCE hesaplanır — gerekçe ComputeDepIssues'ın XML doc'undadır.
        var depIssues = ComputeDepIssues(run, projectId);

        try
        {
            run.Events.TryWrite(new ProjectStartedEvent(run.RunId, projectId, NameOf(run, projectId)));

            InvokeOutcome outcome;
            // [Kısıt 1] Proje logu YALNIZCA bu projenin invoke'u bittikten sonra dispose edilir (dispose
            // sonrası AppendLine fırlatır — satır sessizce düşmez). Ömür BURADA, invoke'un içinde DEĞİL.
            using (var log = run.Logs.OpenProjectLog(projectId))
                outcome = await InvokeOnceAsync(run, projectId, depIssues, log, ct);

            result = outcome.Result;
            durationMs = outcome.DurationMs;
            failReason = outcome.FailReason;
        }
        catch (OperationCanceledException)
        {
            failReason = "stopped";
            failLogTail = " (cancelled)"; // decision.log metni korunur: süre değil, iptal edildiği yazılır
        }
        catch (Exception ex)
        {
            // Invoke/log yolunda beklenmeyen hata: proje tek başına düşer, run devam eder ("hata derlemeyi
            // öldürmez", A3) — ve aşağıdaki finally sayesinde scheduler ASLA askıda kalmaz.
            failReason = "invoke error: " + ex.Message;
            failLogTail = ""; // reason zaten hatayı taşır; satır sonuna ayrıca süre EKLENMEZ
        }
        finally
        {
            ReportProjectResult(run, projectId, result, durationMs, failReason, depIssues,
                trustedResult: true, cycleUnsettled: false, failLogTail);
        }
    }

    /// <summary>
    /// Bir projenin SONUCUNU raporlayan TEK gövde: sonuç olayı + <c>decision.log</c> satırı + BuildState kararı
    /// + <see cref="ReadySetScheduler.Complete"/>. Hem tekil proje yolu (<see cref="BuildProjectAsync"/>) hem SCC
    /// tur döngüsü (<see cref="ReportCycleMember"/>) buradan geçer; iki yol kendi kopyasını taşısaydı
    /// persist/invalidate kuralı ile Complete garantisi sessizce ayrışırdı (kopya YASAK, CLAUDE.md).
    ///
    /// <para><b>Complete <c>finally</c> içindedir ve TAM BİR KEZ çalışır</b>: olay yazımı, decision.log ya da
    /// persist I/O'su beklenmedik biçimde fırlasa bile scheduler askıda kalmaz. Çağıranın tek yükümlülüğü bu
    /// metodu dispatch edilmiş her proje için tam bir kez, KENDİ <c>finally</c>'sinden çağırmaktır.</para>
    /// </summary>
    /// <param name="trustedResult">Bu sonucun ARKASINDA DURULABİLİR mi. Tekil projede daima <c>true</c>. SCC'de
    /// yalnız grup YAKINSADIYSA (<see cref="CycleRoundDecision.Converged"/>) <c>true</c>'dur: turlar bir
    /// bütündür, yakınsamayan bir grubun tur 1'de yeşile dönmüş üyesi de taze imzasını KAYDETMEZ — aksi halde
    /// bir sonraki Build onu "güncel" sayıp atlar ve grup yarım kalmış hâlde temiz görünürdü (§4 gereği DLL/bin
    /// timestamp'i okunmadığı için bunu yakalayacak başka mekanizma yoktur). <c>false</c> ⇒ persist YOK ve
    /// BAŞARILI üye dahil herkes invalidate edilir.</param>
    /// <param name="cycleUnsettled">[cycle rounds] Tavana dayanmış bir SCC'nin başarılı üyesi ⇒ çıktı bir kuşak
    /// geride olabilir (bkz. <see cref="ProjectSucceededEvent.CycleUnsettled"/>).</param>
    /// <param name="failLogTail">Başarısızlık satırının <c>decision.log</c>'daki SONU; <c>null</c> ⇒ varsayılan
    /// <c>" (Nms)"</c>. İptal yolu <c>" (cancelled)"</c>, invoke-error yolu ise <c>""</c> verir (reason zaten
    /// hatanın kendisidir) — bu iki metin ortak gövdeye taşınırken DEĞİŞMEDEN korunur.</param>
    private void ReportProjectResult(RunContext run, string projectId, BuildResult result, long durationMs,
        string? failReason, DepIssueResult depIssues, bool trustedResult, bool cycleUnsettled, string? failLogTail)
    {
        string name = NameOf(run, projectId);
        IReadOnlyList<string>? depIssuesForEvent = depIssues.All.Count > 0 ? depIssues.All : null;
        try
        {
            if (result == BuildResult.Succeeded)
            {
                run.StoppedFailedIds.TryRemove(projectId, out _); // [Task-13] artık Failed değil — eski işaret geçersiz
                // [A2] depIssue TAŞIYAN bir success, bağımlılığının BAYAT (bu run'da derlenmemiş, önceki) çıktısına
                // link'lidir — "başarılı" olması binary'nin güncel olduğu anlamına GELMEZ. Buna rağmen taze imza
                // persist edilirse, failed upstream KAYNAK DEĞİŞMEDEN düzeldiğinde (ör. zehirli obj temizliği) bu
                // projenin imzası da değişmez ve bir sonraki incremental Build onu "güncel" sayıp pre-skip eder;
                // proje sonsuza dek bayat binary'e link'li kalır. §4 gereği DLL/bin timestamp'i OKUNMADIĞI için
                // bunu yakalayabilecek başka bir mekanizma yoktur. Persist etmemenin bedeli "gereksiz bir kez daha
                // derlemek", persist etmenin bedeli "sessizce yanlış çıktı" — güvenli taraf seçilir.
                // [A2 fix-4] Aynı ayrımı ikinci kez TÜRETME: depIssuesForEvent zaten "depIssue var mı" sorusunun
                // materyalize edilmiş hâlidir — depIssue şekli değişirse ikisi kilit adım kalsın.
                // [cycle rounds] trustedResult aynı kuralın ikinci kapısıdır: yakınsamayan grup persist ETMEZ.
                if (trustedResult && depIssuesForEvent is null)
                    PersistBuildStateOnSuccess(run, projectId, durationMs); // [Task 19] sonraki Build incremental olsun
                run.Events.TryWrite(new ProjectSucceededEvent(run.RunId, projectId, durationMs, depIssuesForEvent, cycleUnsettled));
                Decide(run.Logs, string.Format(CultureInfo.InvariantCulture,
                    "{0}: succeeded ({1}ms)", name, durationMs));
            }
            else
            {
                string reason = failReason!;
                MarkStoppedFailed(run, projectId, reason); // [Task-13] Continue'un torn-DLL guard'ı için izlenir
                run.Events.TryWrite(new ProjectFailedEvent(run.RunId, projectId, durationMs, reason, depIssuesForEvent));
                Decide(run.Logs, string.Format(CultureInfo.InvariantCulture, "{0}: failed — {1}{2}", name, reason,
                    failLogTail ?? string.Format(CultureInfo.InvariantCulture, " ({0}ms)", durationMs)));
            }
        }
        finally
        {
            run.Scheduler.Complete(projectId, result); // ÖNCE: her yoldan TAM BİR KEZ, hiçbir I/O bunu geciktiremez
            // [A2 fix-1] BAŞARISIZ biten HER yol (exit!=0 / timeout / stopped / invoke error) stored BuildState'i
            // GEÇERSİZLEŞTİRİR. Aksi halde tek yazıcı PersistBuildStateOnSuccess olduğu için başarısız bir proje
            // kaydına HİÇ dokunmaz: dün Succeeded+eşleşen imzayla yazılmış kayıt bugün proje FAIL etse bile
            // aynen durur ve bir sonraki Build onu "skipped — up to date" diye PRE-SKIP eder — kullanıcıya bozuk
            // bir proje "güncel" diye raporlanır. Complete'ten SONRA çağrılır: persist I/O'su beklenmedik bir
            // şekilde fırlasa bile scheduler ASLA askıda kalmaz.
            // [cycle rounds] Arkasında durulamayan bir BAŞARI da (yakınsamayan SCC'nin yeşil üyesi) buradan geçer.
            if (result != BuildResult.Succeeded || !trustedResult) InvalidateBuildStateOnFailure(run, projectId);
        }
    }

    /// <summary>Projenin görünen adı; id her zaman plan'dadır (scheduler aynı plan'dan sürülür) ama arama
    /// FIRLATMAYAN biçimde yapılır: buradan gelecek bir exception Complete'i atlatabilirdi.</summary>
    private static string NameOf(RunContext run, string projectId) =>
        run.NodeById.TryGetValue(projectId, out var node) ? node.Name : projectId;

    /// <summary>
    /// [cycle rounds] Bir SCC'nin tüm yaşam döngüsü. Üyeler her turda build-order sırasıyla ve SIRALI invoke
    /// edilir — paralellik YOK: A, B.dll'i okurken B aynı dosyayı yazıyor olurdu.
    ///
    /// <para>ARA TUR SONUÇLARI YAYILMAZ. SCC tek bir derleme birimidir (§7.3, tek bileşik imza); yarı bitmiş bir
    /// birimi "bitti" saymak progress'i geri götürür ve ETA'yı yanıltır. Yalnız son turun sonucu raporlanır,
    /// süre ise turların TOPLAMIDIR (gerçek maliyet). <see cref="ProjectStartedEvent"/> ise HER turda yayılır:
    /// o üye o an gerçekten derleniyordur.</para>
    ///
    /// <para><see cref="ReadySetScheduler.Complete"/> dispatch edilmiş her üye için TAM BİR KEZ, <c>finally</c>
    /// içinden çağrılır (stop/iptal/beklenmeyen hata dahil) — biri atlanırsa <see cref="ReadySetScheduler.IsDone"/>
    /// asla true olmaz ve run askıda kalır.</para>
    ///
    /// <para><b>Stop'ta grup ile tekil proje SİMETRİK DEĞİLDİR</b> ve bu kasıtlıdır: graceful stop, in-flight
    /// TEK bir projenin bitip persist etmesine izin verir, ama bir SCC'yi İÇİNDE BULUNDUĞU TURUN sonunda
    /// keser — yakınsama iki ardışık yeşil tur gerektirir ve kesilen grup asla yakınsamış sayılmaz (hiçbir şey
    /// persist edilmez, tüm üyeler invalidate olur). Gerekçe "SCC tek bir derleme birimidir": burada "in-flight
    /// iş" tek bir invoke değil, TURLARIN TAMAMIDIR; kalan turları stop'a rağmen sürdürmek Stop'u anlamsız
    /// kılardı (32 üyeli bir grupta 64 invoke daha).</para>
    /// </summary>
    private async Task BuildCycleGroupAsync(RunContext run, IReadOnlyList<string> allMembers, CancellationToken ct)
    {
        // [TryDispatch sözleşmesi] Dispatch ANINDA zaten Completed'ta olan (ör. resume edilmiş bir run'dan
        // devralınan) ya da plan'da karşılığı olmayan üye in-flight'a HİÇ girmedi; onun için Complete çağırmak
        // fırlatırdı. Tur döngüsü bu yüzden yalnız GERÇEKTEN dispatch edilmiş üyeler üzerinde çalışır — ama
        // grup-içi kenar hesabı TÜM üyelere bakar (dairesel kenar, üye terminal olsa da dairesel kalır).
        var completedAtDispatch = run.Scheduler.Completed;
        var members = allMembers
            .Where(id => run.NodeById.ContainsKey(id) && !completedAtDispatch.ContainsKey(id))
            .ToList();
        if (members.Count == 0) return; // savunmacı: TryDispatch en az bir üyeyi in-flight etmeden grup vermez

        // Üyenin turlar boyunca biriken durumu TEK kayıtta (bkz. CycleMemberState) — paralel sözlükler kilit
        // adım kalmak zorundaydı ve ikisi seyrek dolduğu için "kayıt yok" ile "değer yok" karışırdı.
        var state = new Dictionary<string, CycleMemberState>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in members)
            state[id] = new CycleMemberState(ComputeDepIssues(run, id, excludedDeps: allMembers)); // grup-içi kenarlar hariç

        // [DEĞİŞEN KURAL] Yarıda kesilen grupta HİÇBİR üyenin sonucunun arkasında durulamaz: tur 1'de yeşile
        // dönmüş bir üye de Failed raporlanır. Eskiden yalnız reason yazılır, sonuç KORUNURDU — o üye ÖNCEKİ
        // (ara) turunun sonucuyla ProjectSucceededEvent alırdı; tam olarak "ara tur sonucu nihai sonuç diye
        // yayılmaz" kuralının ihlali, üstelik tekil proje yolu iptalde daima Failed("stopped") raporlar.
        // Persist tarafında zaten yürürlükte olan ilkenin (yakınsamayan grup hiçbir şey persist etmez)
        // raporlama kanalındaki karşılığıdır.
        //
        // [I1] Kural, RAPORLAMANIN HEMEN ÖNÜNDE ve TEK yerde uygulanır — üç kesilme yolu (stop'un break'i,
        // iptal, beklenmeyen hata) için ayrı ayrı çağrılmaz. "Yarıda kesildi"nin tanımı zaten tek bir
        // ifadedir: döngü <see cref="CycleRoundDecision.Continue"/> ile bitmiştir (Converged/NoProgress/
        // CapReached ile biten tur GERÇEK bir karardır). Çağrılar dağıtıldığında biri (stop'un break'i)
        // unutulmuştu ve o yoldan yeşil üyeler Succeeded raporlanıyordu; kapı burada olduğu sürece yeni bir
        // kesilme yolu eklemek kuralı sessizce ATLAYAMAZ.
        void FailEveryMember(string reason)
        {
            foreach (string id in members)
            {
                state[id].Result = BuildResult.Failed;
                state[id].FailReason = reason;
            }
        }

        var decision = CycleRoundDecision.Continue;
        // Kesilme gerekçesi: stop/iptal "stopped", beklenmeyen hata kendi metnini taşır — ikisi AYIRT EDİLİR
        // kalır (tekil proje yolundaki failReason ayrımıyla aynı).
        string interruptedReason = "stopped";
        try
        {
            // Her üyenin logu grubun TÜM turları boyunca AÇIK kalır. OpenProjectLog truncate ettiği için
            // (FileMode.Create) tur başına açmak önceki turların logunu silerdi ve satır numaraları her turda
            // 1'e dönerdi. Bir SCC'nin üye sayısı kadar dosya tanıtıcısı açık kalır — 32 üye için kabul edilir.
            foreach (string id in members) state[id].Log = run.Logs.OpenProjectLog(id);

            HashSet<string>? previousFailed = null;
            for (int round = 1; decision == CycleRoundDecision.Continue; round++)
            {
                run.Events.TryWrite(new CycleRoundStartedEvent(
                    run.RunId, members[0], round, CycleRoundPolicy.RoundCap, members.Count));

                var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string id in members)                       // SIRALI — eşzamanlı invoke YOK
                {
                    var member = state[id];
                    run.Events.TryWrite(new ProjectStartedEvent(run.RunId, id, NameOf(run, id)));
                    var outcome = await InvokeOnceAsync(run, id, member.DepIssues, member.Log!, ct);
                    member.DurationMs += outcome.DurationMs;         // süre TURLARIN TOPLAMI
                    member.Result = outcome.Result;
                    if (outcome.Result != BuildResult.Succeeded)
                    {
                        failed.Add(id);
                        member.FailReason = outcome.FailReason;
                    }
                }

                decision = CycleRoundPolicy.Decide(round, failed, previousFailed);
                previousFailed = failed;
                // Stop istendiyse YENİ tur AÇILMAZ. Hard stop in-flight child'ları çoktan öldürmüştür (üyeler
                // "stopped" raporlar) ama job yeni process kabul etmeye devam eder: guard olmasaydı bir sonraki
                // tur öldürülmüş bir turun üstüne TAZE MSBuild child'ları doğururdu — üstelik o turlar yeşile
                // dönebilir, grup "yakınsadı" sayılır ve DURDURULMUŞ bir koşu taze imza persist ederdi.
                // Zaten Converged/NoProgress/CapReached ile biten tur bu daldan geçmez: yakınsama turların
                // TAMAMLANMASINA dayanır, yarıda kesilen grup asla yakınsamış sayılmaz (decision Continue
                // kalır ⇒ ReportCycleMember hiçbir şey persist etmez, her üyeyi invalidate eder).
                if (decision == CycleRoundDecision.Continue && StopRequested) break;
            }
        }
        catch (OperationCanceledException)
        {
            // gerekçe zaten "stopped" — decision Continue'da kaldığı için aşağıdaki kapı uygular
        }
        catch (Exception ex)
        {
            interruptedReason = "invoke error: " + ex.Message;
        }
        finally
        {
            // [I1] Döngü GERÇEK bir kararla bitmediyse grup yarıda kesilmiştir: raporlamadan hemen önce
            // HERKES Failed'a çevrilir (yukarıdaki gerekçe).
            if (decision == CycleRoundDecision.Continue) FailEveryMember(interruptedReason);

            // Loglar sonuç raporlanmadan ÖNCE kapatılır (dispose sonrası AppendLine fırlatır — geç gelen
            // satır sessizce düşmez). Bir dispose fırlarsa diğerleri yine de kapanır ve raporlama çalışır.
            foreach (var member in state.Values)
                if (member.Log is { } log)
                    try { log.Dispose(); } catch { /* log kapanışı raporlamayı engellemez */ }

            // Sonuçlar TEK yerde raporlanır — ara turlarda hiçbir şey yayılmadı.
            //
            // Her üye KENDİ try'ındadır: buradan kaçan bir exception (en ulaşılabilir tetikleyici, üye
            // filtresi TryDispatch'in kuralından kayarsa Complete'in InvalidOperationException'ı; ikincil
            // olarak Decide'ın yalnız IOException yakalaması) SONRAKİ üyeleri Complete EDİLMEDEN bırakırdı ve
            // IsDone'ın "_inFlight.Count == 0" şartı hiç sağlanmayacağı için run FAIL etmez, ASILIRDI — yani
            // gürültülü bir sözleşme ihlali sessiz bir kilitlenmeye dönerdi. Hiçbir şey YUTULMAZ: ilk
            // exception saklanır ve döngüden sonra stack'i korunarak yeniden fırlatılır. Bunu bir finally
            // içinde yapmak güvenlidir — yukarıdaki iki catch TÜM exception'ları tükettiği için bu noktada
            // bekleyen (üzerine yazılabilecek) bir exception asla yoktur.
            Exception? reportFailure = null;
            foreach (string id in members)
            {
                var member = state[id];
                try
                {
                    ReportCycleMember(run, id, member.Result, member.DurationMs, member.FailReason,
                        decision, member.DepIssues);
                }
                catch (Exception ex) { reportFailure ??= ex; }
            }
            // [Task 7] Üye raporlamasından SONRA: yukarıdaki döngü zaten her üyeyi invalidate etmiştir
            // (trustedResult=false ⇒ InvalidateBuildStateOnFailure), bu yalnız ÜZERİNE, hangi bileşik imzada
            // pes edildiğini ayrıca kaydeder. reportFailure varsa bile denenir — bir üyenin raporlama hatası
            // hafıza yazımını ENGELLEMEMELİDİR (aksi halde bir sonraki Build yine boşuna turlar tüketirdi).
            RecordCycleOutcome(run, allMembers, members, decision);
            if (reportFailure is not null) ExceptionDispatchInfo.Capture(reportFailure).Throw();
        }
    }

    /// <summary>
    /// [Task 7] Bir SCC'nin NİHAİ kararını kayda geçirir: <c>decision.log</c> satırı (grubun neden durduğu —
    /// aksi halde logda yalnız üye-başına <c>failed — exit 1</c> satırları görünür ve operatör "tavan mı, ilerleme
    /// yokluğu mu" ayrımını YAPAMAZ) + yakınsamama hafızası.
    ///
    /// <para><b>decision hâlâ <see cref="CycleRoundDecision.Continue"/>'daysa (stop/iptal/beklenmeyen hata
    /// yüzünden yarıda kesilmiş grup) hiçbir şey yazılmaz:</b> bir Stop ya da geçici bir hata "bu SCC asla
    /// yakınsamaz" anlamına GELMEZ — bir sonraki run yine gerçek bir deneme hakkı almalıdır (kesilme zaten
    /// üyelerin kendi <c>failed — stopped</c> satırlarından okunur).</para>
    /// </summary>
    private void RecordCycleOutcome(RunContext run, IReadOnlyList<string> allMembers,
                                    IReadOnlyList<string> members, CycleRoundDecision decision)
    {
        if (decision == CycleRoundDecision.Continue) return;

        // Logda grubu ANAN ad, CycleRoundStartedEvent'in lideriyle AYNI olmalıdır (build-order'daki ilk üye) —
        // yoksa aynı grup iki kanalda iki farklı adla anılırdı. Bu, aşağıdaki İMZA temsilcisinden ayrı bir
        // sorudur: o "hangi üyenin imzası okunacak", bu "kullanıcı grubu hangi adla görüyor".
        string leader = NameOf(run, members[0]);
        string? remembered = UpdateCycleNonConvergenceMemory(run, allMembers, members, decision);
        Decide(run.Logs, string.Format(CultureInfo.InvariantCulture, "cycle {0}: {1} ({2} members){3}",
            leader, CycleOutcomeText(decision), members.Count,
            remembered is null ? "" : "; non-convergence remembered at " + remembered));
    }

    /// <summary>[Task 7] Tur kararının kullanıcıya dönük (İngilizce) karşılığı — tavan sayısı literal DEĞİL,
    /// tek kaynak <see cref="CycleRoundPolicy"/>'dir.</summary>
    private static string CycleOutcomeText(CycleRoundDecision decision) => decision switch
    {
        CycleRoundDecision.Converged => "converged",
        CycleRoundDecision.NoProgress => "no progress — the same members failed twice",
        CycleRoundDecision.CapReached => string.Format(CultureInfo.InvariantCulture,
            "round cap reached ({0} rounds) — output may be one generation behind", CycleRoundPolicy.RoundCap),
        _ => "interrupted",
    };

    /// <summary>
    /// [Task 7] Yakınsamama hafızasının TEK yazıcısı — <see cref="BuildState.NonConvergentSignature"/>.
    ///
    /// <para><b>YALNIZ <see cref="CycleRoundDecision.NoProgress"/> ⇒ YAZ.</b> TÜM üyelerin alanına o anki
    /// bileşik imza yazılır; bir sonraki <c>Build</c> aynı imzayı görürse grup <see
    /// cref="BuildStateStore.IsCycleNonConvergent"/> ile pre-skip edilir, kaynak DEĞİŞMEDEN turlar tekrar
    /// TÜKETİLMEZ. Kayıt hiç yoksa (SCC hiç derlenmemiş) burada taze bir <see cref="BuildState"/> açılır —
    /// <see cref="InvalidateBuildStateOnFailure"/> yalnız MEVCUT kayıtları günceller, yenisini AÇMAZ.</para>
    ///
    /// <para><b><see cref="CycleRoundDecision.CapReached"/> ⇒ YAZMA.</b> İki karar aynı şey DEĞİLDİR ve ayrım
    /// KANITA dayanır. NoProgress "aynı küme iki tur üst üste patladı" demektir: tur eklemek çözmez, bu bir
    /// SIKIŞMA kanıtıdır ve yeniden denemeyi reddetmeyi haklı çıkarır. CapReached ise "hâlâ hareket var ama
    /// BÜTÇE bitti" demektir — kanıt değil, kesinti. Hatırlansaydı tavanın kendi gerekçesi geçersiz olurdu:
    /// tavan "bilgi kaybettirmez, çünkü turlar diskteki duruma göre idempotenttir ve bir sonraki <c>Build</c>
    /// kaldığı yerden devam eder" diyerek meşrudur, ama pre-skip edilen bir grupta o devam HİÇ gelmez. Tam
    /// olarak yakınsamakta olan bir grup (tur1 {A,B}, tur2 {A}, tur3 temiz) bir tur kala donar ve tek çıkış
    /// ilgisiz bir kaynak değişikliği olurdu. Bedel kabul edilmiştir: dört-altı tur isteyen bir döngü,
    /// oturana dek sonraki birkaç Build'de de turlarını harcar.</para>
    ///
    /// <para><b>Converged (ve CapReached) ⇒ SİL.</b> [M3] Yakınsama hafızayı geçersiz kılar. Bunun ÇOĞU üye için zaten bir yan
    /// etkisi vardır (<see cref="PersistBuildStateOnSuccess"/> taze bir <see cref="BuildState"/> KURAR, alan
    /// doğal olarak null'a döner) ama o yol dep-issue TAŞIYAN bir üyede bilerek çalışmaz ([A2] kapısı): grup
    /// yakınsasa bile o üyenin eski imzası kayıtta kalır, ve <c>Build</c>'in pre-skip'i TÜM üyeleri
    /// istediği için grup aynı imzada sonsuza dek pre-skip edilirdi — kaynak değişmeden çıkışı olmayan bir
    /// tuzak. Bu yüzden silme AÇIKÇA burada, hafızanın kendi yazıcısında yapılır (yan etkiye bırakılmaz).</para>
    ///
    /// <para><b>İmza temsilcisi</b> <see cref="CycleGroups.SignatureRepresentative"/>'dendir ve OKUYAN taraf
    /// (Build'in pre-skip taraması) AYNI yardımcıyı çağırır — iki taraf kendi <c>[0]</c>'ını seçseydi listeler
    /// farklı sıralı olduğu için üye-başına imzanın ayrıştığı modda farklı imzalara bakarlardı. <b>Safe</b>
    /// (App'in gönderdiği tek mod) modda zaten TÜM üyeler AYNI bileşik imzayı taşır, bu yüzden seçim orada
    /// önemsizdir; <b>Fast</b> modda ise üyeler ortak imza taşımaz (<c>IncrementalPlanner</c> bileşen haritasını
    /// yalnız Safe'te kurar) — orada bu hafıza pratikte İŞLEMEZ (okuyanın <c>All</c> koşulu tutmaz) ve bu
    /// bilinçli olarak böyle bırakılmıştır.</para>
    ///
    /// <para>Persist I/O hatası run'ı ÖLDÜRMEZ (warn-only) ve filtre KOŞULSUZDUR: bu metot
    /// <see cref="BuildCycleGroupAsync"/>'in <c>finally</c>'sinden çağrılır ve üstünde umbrella bir <c>catch</c>
    /// YOKTUR — buradan kaçan herhangi bir exception (yalnız I/O değil: bozuk JSON, serialization, güvenlik)
    /// worker'ı sessizce öldürür ve sonuncusuysa run ASILIR. Gerekçe kardeş yazıcı
    /// <see cref="InvalidateBuildStateOnFailure"/>'da uzun uzun yazılıdır.</para>
    /// </summary>
    /// <returns>Hafızaya YAZILAN imza; yazılmadıysa (Converged/CapReached, store/imza yok, I/O hatası)
    /// <c>null</c>.</returns>
    private string? UpdateCycleNonConvergenceMemory(RunContext run, IReadOnlyList<string> allMembers,
                                                    IReadOnlyList<string> members, CycleRoundDecision decision)
    {
        if (run.StateStore is null) return null;
        // Kapı TEK karar: yalnız NoProgress bir SIKIŞMA kanıtıdır. CapReached buradan geçmediği için aşağıdaki
        // temizleme dalına düşer — yani eski bir NoProgress hafızası da SİLİNİR. Bu kasıtlıdır: Rebuild ile
        // aynı imzada koşup tavana dayanan bir grup, bayat hafızası duruyorken bir sonraki Build'de yine
        // pre-skip edilirdi ve tavanın "sonraki Build devam eder" güvencesi ikinci kez delinirdi.
        bool nonConvergent = decision is CycleRoundDecision.NoProgress;

        string? signature = null;
        if (nonConvergent && (run.Incremental is not { } inc
            || CycleGroups.SignatureRepresentative(allMembers) is not { } representative
            || !inc.SignatureById.TryGetValue(representative, out signature)))
            return null;

        try
        {
            var existing = run.StateStore.Load();
            foreach (string id in members)
            {
                if (nonConvergent)
                {
                    var current = existing.TryGetValue(id, out var found)
                        ? found
                        : new BuildState(id, BuiltSignature: null, LastResult: BuildResult.Failed, LastRunAt: DateTimeOffset.UtcNow);
                    run.StateStore.Upsert(current with { NonConvergentSignature = signature });
                }
                // Converged/CapReached: yalnız GERÇEKTEN hafızalı kayıtlara dokunulur — "kayıt yok" ile "alanı
                // zaten boş kayıt" bu soru için AYNI anlama gelir, gereksiz yazım store'u şişirmekten başka
                // iş yapmaz.
                else if (existing.TryGetValue(id, out var found) && found.NonConvergentSignature is not null)
                    run.StateStore.Upsert(found with { NonConvergentSignature = null });
            }
            return signature;
        }
        catch (Exception ex)
        { console("warning: cycle non-convergence memory could not be written: " + ex.Message); return null; }
    }

    /// <summary>
    /// [cycle rounds] Bir SCC üyesinin turlar boyunca biriken durumu. Tek kayıt: alanlar kilit adım ilerlemek
    /// ZORUNDA (sonuç ile gerekçe, süre ile sonuç) ve iki tanesi seyrek dolar — beş paralel sözlükte "anahtar
    /// yok" ile "değer yok" birbirine karışır, biri güncellenirken diğerinin unutulması işten değildi.
    /// </summary>
    /// <param name="depIssues">Grup-içi kenarlar hariç tutularak grup başında BİR KEZ hesaplanır: kardeş
    /// üyeler tur boyunca Completed'a girmediği için yeniden hesaplamak aynı sonucu verirdi.</param>
    private sealed class CycleMemberState(DepIssueResult depIssues)
    {
        /// <summary>SON turun sonucu. Hiç invoke edilemediyse (ör. log açılamadı) Failed kalır.</summary>
        public BuildResult Result { get; set; } = BuildResult.Failed;

        /// <summary>TÜM turların TOPLAM süresi — gerçek maliyet.</summary>
        public long DurationMs { get; set; }

        /// <summary><see cref="Result"/> Succeeded değilken raporlanacak gerekçe.</summary>
        public string? FailReason { get; set; }

        public DepIssueResult DepIssues { get; } = depIssues;

        /// <summary>Grubun tüm turları boyunca açık kalan proje logu; açılamadıysa null.</summary>
        public ProjectLogFile? Log { get; set; }
    }

    /// <summary>
    /// [cycle rounds] Bir SCC üyesinin NİHAİ sonucunu raporlar; tekil projeyle AYNI gövdeden
    /// (<see cref="ReportProjectResult"/>) geçer — sonuç olayı + decision.log + persist kararı + Complete.
    /// Turların hiçbirinde ara sonuç yayılmadığı için bu, o üye hakkında yayılan TEK sonuçtur ve
    /// <paramref name="totalDurationMs"/> turların TOPLAMIDIR.
    /// </summary>
    private void ReportCycleMember(RunContext run, string projectId, BuildResult result, long totalDurationMs,
                                   string? failReason, CycleRoundDecision decision, DepIssueResult depIssues) =>
        ReportProjectResult(run, projectId, result, totalDurationMs, failReason, depIssues,
            // YAKINSAMAYAN GRUP HİÇBİR ŞEY PERSIST ETMEZ: yalnız Converged'e güvenilir. NoProgress/CapReached/
            // stop/iptal/beklenmeyen hata (decision hâlâ Continue) hâlinde yeşil görünen üye de invalidate edilir.
            trustedResult: decision == CycleRoundDecision.Converged,
            // Tavana dayanıldı ve üye yeşil: derleme başarılı ama çıktı bir kuşak geride OLABİLİR. Dep-issue
            // listesine sahte isim enjekte EDİLMEZ — ayrı bir bayrak taşınır.
            cycleUnsettled: decision == CycleRoundDecision.CapReached && result == BuildResult.Succeeded,
            failLogTail: null);

    /// <summary>
    /// [cycle rounds] Tek bir MSBuild invoke'u — komut satırları, depIssue warn satırları dahil. Event YAYMAZ,
    /// Complete ÇAĞIRMAZ, BuildState PERSIST ETMEZ: bunlar çağıranın kararıdır. <paramref name="log"/> zaten
    /// açık gelir, ömrü çağıranındır — gerekçe gövdedeki [Kısıt 1] notunda.
    ///
    /// Neden ayrı: SCC tur döngüsü aynı projeyi birden çok kez invoke eder ama sonucu YALNIZ son turda
    /// raporlar. İki yol aynı invoke gövdesini paylaşmazsa komut satırı/log/retry davranışı sessizce
    /// ayrışırdı (kopya YASAK, CLAUDE.md).
    /// </summary>
    private async Task<InvokeOutcome> InvokeOnceAsync(
        RunContext run, string projectId, DepIssueResult depIssues, ProjectLogFile log, CancellationToken ct)
    {
        var request = new MsBuildInvokeRequest(
            ProjectId: projectId,
            Configuration: run.Configuration,
            SolutionDir: SolutionDirResolver.Resolve(projectId, run.SolutionRefs.GetValueOrDefault(projectId, [])),
            NeedsRestore: HasPackagesConfig(projectId),
            // [I2-K2/Task 10] worktree kökü verilmişse proje-Id başına izole obj; aksi halde in-place =
            // projenin kendi (VS-parity) obj'i — bkz. RunCoordinator ctor'daki worktreeObjRootResolver doc'u.
            BaseIntermediateOutputPath: run.WorktreeObjRoot is not null
                ? WorktreeObjPathResolver.Resolve(run.WorktreeObjRoot, projectId)
                : null);

        // [Kısıt 1] Proje logunu bu metot AÇMAZ ve KAPATMAZ — ömrü çağıranındır: OpenProjectLog
        // FileMode.Create ile truncate ettiği için, log'u burada açmak tur döngüsünde önceki turların
        // logunu siler ve satır numaralarını her turda 1'e döndürürdü.
        // v7Δ-7: proje logunun İLK satırı, bu proje için çalıştırılacak GERÇEK MSBuild komut satırıdır —
        // depIssue uyarıları bu invaryantı BOZMAZ, komut satır(lar)ından SONRA, gerçek derleme çıktısından
        // ÖNCE (log başı) yazılır [T54].
        foreach (string commandLine in CommandLines(request, run.MsBuildExePath))
            Emit(run, projectId, log, commandLine);
        foreach (string warnLine in DepIssueWarnLines(depIssues))
            Emit(run, projectId, log, warnLine);
        var invoke = await run.Invoker.InvokeAsync(request, line => Emit(run, projectId, log, line), ct);

        return invoke.ExitCode == 0 && !invoke.TimedOut && !invoke.Killed
            ? new InvokeOutcome(BuildResult.Succeeded, invoke.DurationMs, null)
            : new InvokeOutcome(BuildResult.Failed, invoke.DurationMs, ReasonFor(invoke));
    }

    /// <summary>
    /// [T54] Dispatch anında TÜM bağımlılıklar terminaldir, bu yüzden depIssues invoke'tan ÖNCE güvenle
    /// hesaplanır ve üç tüketiciye birden verilir (log warn satırları, event, bu projenin dependent'larının
    /// miras alacağı birikim).
    ///
    /// [cycle rounds] <paramref name="excludedDeps"/> grup-içi kenarlar içindir: bir SCC dispatch edildiğinde
    /// kardeş üyeler henüz Completed'ta DEĞİLDİR, dolayısıyla dep-issue hesabına girmemelidirler — aksi halde
    /// her üye kardeşlerini "çözülmemiş" sayıp yanlış uyarı üretirdi. Tekil projede null geçilir.
    /// </summary>
    private DepIssueResult ComputeDepIssues(RunContext run, string projectId,
                                            IReadOnlyList<string>? excludedDeps = null)
    {
        run.NodeById.TryGetValue(projectId, out var node);
        var dependencies = node?.Dependencies ?? [];
        if (excludedDeps is { Count: > 0 })
            dependencies = [.. dependencies.Where(
                d => !excludedDeps.Contains(d, StringComparer.OrdinalIgnoreCase))];

        var depIssues = DepIssueTracker.Compute(
            dependencies,
            run.Scheduler.Completed,
            run.DepIssuesById,
            id => run.NodeById.TryGetValue(id, out var n) ? n.Name : id);
        run.DepIssuesById[projectId] = depIssues.All;
        return depIssues;
    }

    /// <summary>
    /// [T54] Proje logunun BAŞINDA (komut satırlarından hemen sonra, gerçek derleme çıktısından ÖNCE) yazılan
    /// depIssue uyarı satırları. DOĞRUDAN her failed bağımlılık için AYRI bir satır ("X failed in this run —
    /// last successful output referenced (X)"): bu projenin doğrudan bağımlılığı olan X bu run'da failed oldu,
    /// dolayısıyla X'in ÖNCEKİ (başarılı) çıktısı referanslanıyor. DOLAYLI (bu projenin doğrudan bağımlılığı
    /// OLMAYAN, zincirden miras alınan) kökler TEK birleşik satırda toplanır — CS0006 zincirinde ara katmanların
    /// her biri aynı kökü tekrar tekrar uyarmasın diye. Hiç depIssue yoksa hiçbir satır YOK.
    /// </summary>
    private static IEnumerable<string> DepIssueWarnLines(DepIssueResult depIssues)
    {
        foreach (string root in depIssues.Direct)
            yield return $"warning: {root} failed in this run — last successful output referenced ({root})";
        if (depIssues.Indirect.Count > 0)
            yield return $"warning: failure in dependency chain ({string.Join(", ", depIssues.Indirect)}) — referenced outputs may be stale";
    }

    /// <summary>Satırı diske yazar (1-tabanlı satır no) ve aynı numarayla canlı <c>projectLog</c> olayı üretir.</summary>
    private static void Emit(RunContext run, string projectId, ProjectLogFile log, string line)
    {
        int lineNumber = log.AppendLine(line);
        run.Events.TryWrite(new ProjectLogEvent(run.RunId, projectId, lineNumber, RunLogWriter.SanitizeLine(line)));
    }

    private static IEnumerable<string> CommandLines(MsBuildInvokeRequest request, string msbuildExePath)
    {
        if (request.NeedsRestore) // restore ÖNCE koşar (bkz. MsBuildInvoker) — komut satırı da o sırada yazılır
            yield return WindowsCommandLine.Build(msbuildExePath,
                [.. MsBuildArguments.RestorePackagesConfig(request.ProjectId, request.SolutionDir)]);
        yield return WindowsCommandLine.Build(msbuildExePath,
            [.. MsBuildArguments.Build(request.ProjectId, request.Configuration, request.BaseIntermediateOutputPath)]);
    }

    /// <summary>
    /// [Task-13] <paramref name="reason"/>=="stopped" ise <paramref name="projectId"/>'i run'lar arası devredilen
    /// <c>StoppedFailedIds</c> birikimine yazar (torn-DLL guard — bkz. Continue'un RetryPlanning.RequeueStoppedFailed
    /// çağrısı); değilse (savunmacı — id daha önce stopped işaretliyken şimdi FARKLI bir reason'la Failed olduysa)
    /// siler, aksi halde stale bir "stopped" izi Continue'da yanlışlıkla yeniden derlemeye yol açardı.
    /// </summary>
    private static void MarkStoppedFailed(RunContext run, string projectId, string reason)
    {
        if (reason == "stopped") run.StoppedFailedIds[projectId] = 0;
        else run.StoppedFailedIds.TryRemove(projectId, out _);
    }

    /// <summary>
    /// [Task 19] Bir proje BAŞARIYLA derlendiğinde <see cref="BuildState"/> persist eder — BİR SONRAKİ Build
    /// koşusu bunu okuyup (imza eşit + Succeeded ⇒ skip) incremental olur. Persist YALNIZ hem <see
    /// cref="RunContext.StateStore"/> hem de bu proje için non-null bir imza (<see cref="IncrementalPlan"/>)
    /// varsa yapılır (testlerdeki basit planner → Incremental null → persist YOK, davranış nötr). [A2] Çağıran
    /// AYRICA "depIssue taşımayan success" koşulunu uygular (bkz. BuildProjectAsync'teki gerekçe). §4: yalnız
    /// build-state.json'a yazılır, DLL/bin/obj'ye dokunulmaz. Persist I/O hatası run'ı ÖLDÜRMEZ (warn-only).
    /// </summary>
    private void PersistBuildStateOnSuccess(RunContext run, string projectId, long durationMs)
    {
        if (run.StateStore is null || run.Incremental is not { } inc
            || !inc.SignatureById.TryGetValue(projectId, out var signature))
            return;

        var state = new BuildState(projectId, signature, inc.HeadCommit, BuildResult.Succeeded,
            DateTimeOffset.UtcNow, inc.Branch, durationMs);
        try { run.StateStore.Upsert(state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { console("warning: build-state could not be written (" + Path.GetFileNameWithoutExtension(projectId) + "): " + ex.Message); }
    }

    /// <summary>
    /// [A2 fix-1] Bir proje BAŞARISIZ bittiğinde stored <see cref="BuildState"/>'i GEÇERSİZLEŞTİRİR:
    /// <c>LastResult=Failed</c> yazılır, böylece <see cref="Core.Planning.WillBuildEvaluator"/>
    /// (<c>LastResult != Succeeded ⇒ WillBuild=true</c>) bir sonraki Build'de bu projeyi "up to date" sayıp
    /// pre-skip EDEMEZ. §4 gereği DLL/bin timestamp'i okunmadığı için invalidasyonun tek yeri burasıdır.
    /// <para>
    /// <b>Neden HER başarısızlık türü:</b> stopped/timeout/invoke-error de "bilinen iyi" DEĞİLDİR — yarıda
    /// kesilmiş bir derleme torn/eksik çıktı bırakabilir. Güvenli yön invalidasyondur; bedeli "gereksiz bir kez
    /// daha derlemek", diğer yönün bedeli "sessizce bozuk çıktıyı güncel sanmak".
    /// </para>
    /// <para>
    /// <b>Partial merge</b> (<see cref="Core.State.BuildDurationPersister"/> deseni): <see
    /// cref="BuildState.BuiltSignature"/>/<see cref="BuildState.BuiltCommit"/>/<see cref="BuildState.LastBranch"/>/
    /// <see cref="BuildState.LastDurationMs"/> DOKUNULMADAN korunur. Gerekçe: (1) imza, Fast (frozen-upstream)
    /// modda dependent'ların karşılaştırma tabanıdır — null'lanırsa bu projeye bağımlı HER proje de gereksizce
    /// dirty olurdu; (2) <c>LastDurationMs</c> ETA tahminini besler, bir başarısızlığın (çoğu zaman erken patlayan)
    /// süresi İYİ bir ölçümün üzerine yazılmamalıdır. Yalnız <c>LastResult</c>/<c>LastRunAt</c> güncellenir.
    /// </para>
    /// <para>
    /// Kayıt YOKSA hiçbir şey yazılmaz: "kayıt yok" ile "imzası olmayan kayıt" tüm tüketiciler için AYNI anlama
    /// gelir (WillBuild=true) — boş satır eklemek store'u şişirmekten başka bir şey yapmaz.
    /// </para>
    /// Persist I/O hatası run'ı ÖLDÜRMEZ (warn-only).
    /// <para>
    /// [A4 review fix] Bu metot <see cref="BuildProjectAsync"/>'in <c>finally</c>'sinden çağrılır ve
    /// <see cref="WorkerAsync"/>'in <c>try/finally</c>'sinin ÜSTÜNDE hiçbir umbrella <c>catch</c> yoktur —
    /// buradan kaçan HERHANGİ bir exception (yalnız I/O değil: bozuk JSON, serialization, vb.) worker'ı
    /// sessizce öldürür ve sonuncusuysa kuyrukta dispatch edilmemiş projelerle run ASILIR. Bir <c>finally</c>
    /// içinde "run'ı öldürmez" sözü ancak KOŞULSUZ olabilir; bu yüzden filtre daraltılmaz.
    /// </para>
    /// </summary>
    private void InvalidateBuildStateOnFailure(RunContext run, string projectId)
    {
        if (run.StateStore is null) return;
        try
        {
            if (!run.StateStore.Load().TryGetValue(projectId, out var existing)) return; // geçersizleştirilecek kayıt yok
            run.StateStore.Upsert(existing with { LastResult = BuildResult.Failed, LastRunAt = DateTimeOffset.UtcNow });
        }
        catch (Exception ex)
        { console("warning: build-state could not be invalidated (" + Path.GetFileNameWithoutExtension(projectId) + "): " + ex.Message); }
    }

    private string ReasonFor(MsBuildInvokeResult invoke)
    {
        // Hard stop ÖNCE bakılır: TerminateJobObject child'ı öldürdüğünde invoke sıradan bir "exit N" gibi döner
        // (OperationCanceledException DEĞİL) — bu, kullanıcının bilinçli Stop'udur, projenin hatası değil.
        lock (_gate)
        {
            if (_stopKind == StopKind.Hard) return "stopped";
        }
        if (invoke.TimedOut) return "timeout";
        if (invoke.Killed) return "stopped";
        return string.Format(CultureInfo.InvariantCulture, "exit {0}", invoke.ExitCode);
    }

    // [I2-K2/S2] Legacy restore sinyali: csproj'un YANINDA packages.config. bin/OutDir'e BAKILMAZ [§4].
    private static bool HasPackagesConfig(string projectId)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(projectId));
        return dir is not null && File.Exists(Path.Combine(dir, "packages.config"));
    }

    /// <summary>Yalnız aktif run YOKKEN log writer'ı kapatır: process kapanırken (bkz. Program) hâlâ koşan bir
    /// run'ın worker'ları altından dosyayı çekmek, yakalayanı olmayan bir exception'a dönüşürdü.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_runActive) return;
            _logs?.Dispose();
            _logs = null;
        }
    }

    private sealed record RunContext(
        string RunId,
        string Configuration,
        IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>> SolutionRefs,
        IReadOnlyDictionary<string, ProjectNode> NodeById,
        ReadySetScheduler Scheduler,
        WakeSignal Wake,
        RunLogWriter Logs,
        ChannelWriter<IpcEvent> Events,
        IMsBuildInvoker Invoker,
        string MsBuildExePath,
        // [I2-K2/Task 10] worktree run + resolver'ın döndüğü kök (bkz. RunCoordinator ctor doc); null ⇒ in-place obj.
        string? WorktreeObjRoot,
        // [T54] projectId → depIssues birikimi (RunSegmentAsync'te kurulur, Continue segmentleri boyunca aynı
        // örnek paylaşılır). ConcurrentDictionary: N worker aynı anda FARKLI key'lere yazar, birbirinin key'ini okur.
        ConcurrentDictionary<string, IReadOnlyList<string>> DepIssuesById,
        // [Task-13] projectId → "şu an Failed VE reason=stopped" işareti (BuildProjectAsync tarafından yazılır/
        // silinir — bkz. o metodun sonundaki not). Continue segmentinin torn-DLL guard'ı için: RunSegmentAsync
        // bunu Completed'tan Queued'a geri taşımak üzere okur (bkz. RetryPlanning.RequeueStoppedFailed).
        ConcurrentDictionary<string, byte> StoppedFailedIds,
        // [Task 19] projectSucceeded → BuildState persist hedefi (null ⇒ persist YOK); imza/HEAD/branch kaynağı.
        // [A2 fix-1] AYRICA projectFailed → mevcut kaydın LastResult'ı Failed'a çekilir (stale pre-skip'i keser).
        BuildStateStore? StateStore,
        IncrementalPlan? Incremental,
        // [cycle rounds] SCC üyelik haritası — <see cref="ReadySetScheduler"/>'a verilenin AYNI örneği (null ⇒
        // plan'da SCC yok ya da kill switch kapalı; o zaman worker yalnız tekil proje yolunu kullanır).
        CycleGroups? Groups);

    /// <summary>
    /// Park etmiş worker'ları toplu uyandıran async sinyal — <c>SemaphoreSlim</c>/sleep-poll YOK [D8].
    /// <see cref="Waiter"/> ile alınan Task, bir sonraki <see cref="WakeAll"/>'da tamamlanır; her uyandırmada
    /// TCS atomik olarak yenilenir, böylece sinyal "tek kullanımlık" değil tekrarlanabilir olur.
    /// </summary>
    private sealed class WakeSignal
    {
        private TaskCompletionSource _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Waiter => Volatile.Read(ref _tcs).Task;

        public void WakeAll() =>
            Interlocked.Exchange(ref _tcs, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously))
                .TrySetResult();
    }
}
