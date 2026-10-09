using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using Stopwatch = System.Diagnostics.Stopwatch;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Diagnostics;
using BuildOrchestrator.Core.Externals;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Io;
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
/// [Task 19 wiring] Her koşunun incremental karar verileri: her projenin planlama
/// anında hesaplanmış <see cref="Contracts.Model.BuildSignature"/> (byte-stable) imzası + HEAD commit + branch.
/// <see cref="RunCoordinator"/> bir proje <c>projectSucceeded</c> olduğunda bu bilgiyle <see
/// cref="Core.State.BuildStateStore"/>'a <see cref="BuildState"/> persist eder — böylece BİR SONRAKİ Build
/// incremental olur. <c>null</c> Incremental (ör. testlerdeki basit planner) → persist YOK, pre-skip YOK.
/// Clean modunda ise başarı persist EDİLMEZ, kayıt SİLİNİR (<c>ForgetBuildStateOnClean</c>).
/// [A2 fix-1] Bu yalnız BAŞARI yolu içindir: başarısızlıkta yapılan invalidasyon (bkz.
/// <c>InvalidateBuildStateOnFailure</c>) imza/HEAD gerektirmez, mevcut kaydı yerinde günceller.
/// </summary>
/// <param name="CommitByProjectId">[design v1.14.0 §9] Proje başına revizyon ÜSTÜNE YAZMASI — yalnız harici
/// köklerden gelen projeler için dolar (bkz. <see cref="ExternalRevisionReader"/>). <see cref="HeadCommit"/>
/// ANA REPOYU anlatır; harici bir projenin kaydına onu yazmak başka bir reponun commit'ini o satırın sha
/// yuvasında göstermek olurdu.</param>
/// <param name="ContentById">[v1.16.0] Proje → KENDİ girdi dosyalarının içerik özeti. İki yere gider:
/// başarılı bir derlemede deftere (<see cref="BuildState.BuiltContent"/>) ve önizlemeye — satırın
/// <c>modified</c> ↔ <c>affected</c> ayrımı, deftere yazılmış özetle bugünkünün karşılaştırmasıdır.</param>
/// <param name="OutputsById">[Faz 3/Task 4 — spec 2026-09-18 §5.1] Proje → çıktı kanıtı + beslenen aday kopyalar
/// (<see cref="Core.Incremental.OutputEvidence.Locate"/>, <see cref="IncrementalRunBinder.OutputsById"/>).
/// Başarılı bir derlemeden sonra <see cref="OutputEvidence.LearnFedOutputs"/> BUNDAN okur ve gerçekten beslenen
/// kopyaları <see cref="BuildState.FedOutputs"/>'a yazar. Kayıt yoksa (testlerdeki basit planner) o proje için
/// öğrenme yapılmaz (<c>null</c> ⇒ <see cref="BuildState.FedOutputs"/> null kalır).</param>
/// <param name="ChecksById">[Faz 3/Task 6 — spec 2026-09-18 §5] Planın kararına giren çıktı kontrolleri
/// (<see cref="IncrementalRunBinder.ChecksFor"/>) — koşu önizlemesi <c>OwnFilesChanged</c>'ı ve
/// <c>OutputBuiltAt</c>'ı Sync ile AYNI yardımcılardan (<see cref="OutputEvidence.OwnFilesChanged(OutputCheck?, IReadOnlyDictionary{string, BuildState}?, string, string?)"/>,
/// <see cref="OutputEvidence.OutputBuiltAt"/>) bundan yazar. <c>null</c> (testlerdeki basit planner) ⇒ kanıtsız.</param>
/// <param name="MemberTermById">[RESOLVE Faz 3/Task 3.1 · D7-b] SCC üyesi → kendi terimi
/// (<see cref="Core.Incremental.IncrementalSignatures.MemberTermById"/>): kardeşlerin içeriğini de grup dışı upstream'lerin
/// imzasını da değil, yalnız üyenin kendi girdilerini (içerik + configuration) anlatan terim — her upstream sabit
/// işarettir; grup dışı upstream'in değişimi kayıttaki bağımlılık yüzeyleriyle denetlenir. Tur 1 grubun İÇİNDE kimin
/// derleneceğini bununla seçer; bileşik imza (<c>SignatureById</c>) DOWNSTREAM ve "grup kirli mi" için kalır. Yalnız SCC
/// üyeleri için dolu (Fast geçişinde boş); <c>null</c> (testlerdeki basit planner) ⇒ üye terimi bilinmiyor.</param>
public sealed record IncrementalPlan(
    IReadOnlyDictionary<string, string> SignatureById,
    string? HeadCommit,
    string? Branch,
    IReadOnlyDictionary<string, string>? CommitByProjectId = null,
    IReadOnlyDictionary<string, string?>? ContentById = null,
    IReadOnlyDictionary<string, ProjectOutputs>? OutputsById = null,
    IReadOnlyDictionary<string, OutputCheck>? ChecksById = null,
    IReadOnlyDictionary<string, string>? MemberTermById = null,
    // [D5] Sırası gelince yüzey kapısından geçecek projeler (SurfaceGate.CandidateIds: Safe ile kanıtlı Fast bağlamanın
    // karşılaştırması); null ⇒ kapı yok. Yalnız defteri dinleyen tam koşuda üretilir (SurfaceGate.AppliesTo).
    IReadOnlySet<string>? SurfaceCandidateIds = null);

/// <summary>
/// Bir run için MSBuild takımı: <b>ham</b> (retry'siz) invoker + çözülmüş MSBuild.exe yolu.
/// Yol, proje logunun İLK satırına yazılan gerçek komut satırını üretmek için gerekir (v7Δ-7).
/// Retry sarmalamasını (<see cref="RetryingMsBuildInvoker"/>) koordinatör yapar: <c>onRetry</c> run'a özgü
/// <c>decision.log</c>'a yazar, o log ise ancak run başlarken var olur.
/// </summary>
/// <param name="CustomBeforeTargetsPath">[WPF geçici assembly] Targets dosyasının tam yolu
/// (<see cref="WpfTemporaryAssemblyTargets.EnsureWritten"/>); her derleme isteğine
/// <see cref="MsBuildInvokeRequest.CustomBeforeTargets"/> olarak taşınır. null ⇒ argüman girmez (dosya yazılamadı ya
/// da testlerdeki sahte takım): derleme bugünkü komut satırıyla sürer.</param>
public sealed record MsBuildToolset(IMsBuildInvoker Invoker, string MsBuildExePath, string? CustomBeforeTargetsPath = null);

/// <summary>
/// [T4/T55] Run'ın yürütme kalbi: plan → N paralel worker → proje-başına <c>MSBuild.exe</c> shell-out →
/// disk log + IPC event → Stop. Planlama YOK (Core'un işi [D3]), in-process MSBuild YOK [§0/§3],
/// bin/OutDir'den yalnız başarılı bir derlemeden sonra beslenen kopya adaylarının boyutu ve zamanı okunur
/// (<see cref="OutputEvidence.LearnFedOutputs"/>), bellek ring buffer YOK — tek log kaynağı disktir [D4].
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
/// bloklamaz).</para></param>
/// <param name="msbuildFactory">MSBuild takımını (ham invoker + exe yolu) LAZY çözer: vswhere/VS yoksa Supervisor
/// yine ayağa kalkar, hata ancak <c>startRun</c>'da <c>error(msbuildNotFound)</c> olarak bildirilir.</param>
/// <param name="logFactory">Run başına TEK <see cref="RunLogWriter"/> üretir; HER koşu kendi writer'ını
/// (kendi run dizinini) açar — "aynı writer'ı paylaşan" bir sonraki koşu senaryosu YOK.</param>
/// <param name="nowMs">MONOTONİK zaman kaynağı (üretimde <c>Environment.TickCount64</c>); duvar saati
/// KULLANILMAZ — geri atlarsa elapsed negatife düşerdi.</param>
/// <param name="console">Konsol (stderr) uyarı/özet kanalı. stdout YALNIZ NDJSON'dır [D4], bu yüzden buradan
/// asla stdout'a yazılmaz.</param>
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
/// <param name="inFlight">
/// [spec 2026-09-18 §5.5 · karar 12] Uçuştaki projelerin defteri (<c>run-inflight.json</c>): dispatch anında
/// <c>Add</c>, sonuç raporlanınca <c>Remove</c>, koşunun her çıkışında <c>Clear</c>. Null ⇒ defter tutulmaz.
/// Defter I/O hatası koşuyu durdurmaz — konsol uyarısıdır.
/// </param>
/// <param name="machine">
/// [PERF Faz D / karar 10] Koşu başında makinenin görüntüsü: (mantıksal işlemci sayısı, boş fiziksel bellek).
/// <see cref="WorkerBudget.Clamp"/> profilin istediği işçi sayısını bu görüntüye göre kırpar. Üretimde (null)
/// <see cref="MachineResources.Snapshot"/>; seam yalnız test içindir — kırpma kararı, testin koştuğu makinenin o anki
/// boş belleğine bağlanmasın diye testler sabit bir makine verir.
/// </param>
public sealed class RunCoordinator(
    Func<StartRunCommand, Action<string>, RunPlan> planner,
    Func<CancellationToken, Task<MsBuildToolset>> msbuildFactory,
    Func<DateTimeOffset, RunLogWriter> logFactory,
    NdjsonWriter writer,
    JobObject innerJob,
    Func<long> nowMs,
    Action<string> console,
    BuildStateStore? stateStore = null,
    ICpuGovernor? cpuGovernor = null,
    Func<TimeSpan, CancellationToken, Task>? retryDelay = null,
    InFlightLedger? inFlight = null,
    Func<string, string?>? apiSurface = null,
    Func<(int Cores, long FreeBytes)>? machine = null) : IDisposable
{
    private readonly object _gate = new();
    private readonly ICpuGovernor _cpu = cpuGovernor ?? innerJob;

    /// <summary>[PERF Faz D / karar 10] Koşu başında makinenin görüntüsü — üretimde <see cref="MachineResources.Snapshot"/>.</summary>
    private readonly Func<(int Cores, long FreeBytes)> _machine = machine ?? MachineResources.Snapshot;

    /// <summary>[API kısa devresi] Bir çıktı dosyasının yüzey özeti — üretimde <see cref="ApiSurfaceHash.OfFile"/>.
    /// Seam yalnız test içindir: gerçek PE üretmeden tur döngüsünün bayatlık kararı kurulabilsin.</summary>
    private readonly Func<string, string?> _apiSurface = apiSurface ?? ApiSurfaceHash.OfFile;

    // --- run yaşam döngüsü (hepsi _gate altında) ---
    private bool _runActive;            // startRun slotu dolu (planlama dahil) — A6
    private bool _finishing;            // sonuç olayları yazılmaya başladı → Stop artık sahiplenilemez
    private Task _runTask = Task.CompletedTask;
    private StopKind? _stopKind;        // null = stop istenmedi; Hard, Graceful'u EZER (geri alınmaz)
    private bool _stopAcked;            // runStopped yazıldı mı — TryRequestStop true dedi ise ACK BORCU vardır
    // [spec 2026-09-18 §6.1 · karar 10] Koşu branch değişimiyle kesildi mi (StopKind.Interrupt). _stopKind'dan
    // AYRI tutulur: kesme bir durdurma TÜRÜ değil, sonuçların güvenilirliği hakkında bir olgudur — Hard sonradan
    // gelse de (Hard kazanır) koşu kesilmiş kalır. Okuyan tek kapı ReportProjectResult'tır.
    private bool _interrupted;
    private ReadySetScheduler? _scheduler;
    private WakeSignal? _wake;
    // [T20-b/K11] Bu run için cap/priority GERÇEKTEN uygulandı mı (yani komut bir PerfMode taşıyor muydu).
    // Yalnız true iken run sonunda geri alınır ve yalnız true iken canlı setPerfMode UYGULANIR: PerfMode
    // taşımayan (P2 öncesi / harness) run'lar job'a hiç dokunmamalıdır.
    private bool _perfApplied;
    // [Fix round 1 — KÖK 1] PLANLAMA PENCERESİNDE gelmiş perf niyeti. startRun kabul edilir edilmez _runActive
    // true olur, ama cap ancak plan kurulduktan SONRA (PlanAndRunAsync'in apply noktası) uygulanır; 177 projede
    // bu pencere SANİYELERDİR ve perf chip'i o sırada canlıdır. Buraya yazılan niyet run başlarken komuttaki
    // PerfMode'u EZER (kullanıcının SON sözü). Apply noktasında tüketilir; [Fix round 2 — YENİ 1] run
    // kapanışında İKİ kez temizlenir (ReleasePerf + son kilit), çünkü ikisinin arasındaki `await pump`
    // penceresinde _runActive hâlâ true'dur ve oraya düşen bir niyeti başka temizleyen olmazdı.
    private PerfProfile? _pendingPerf;
    // [T20-b/P3] Bu run'da YÜRÜRLÜKTEKİ profil — copy-contention penceresi kapanınca cap buraya döner.
    // (_perfApplied false iken anlamsızdır; ikisi de run sınırında birlikte sıfırlanır.)
    private PerfProfile? _activePerf;
    // [RESOLVE Faz 4 / karar 11] Bu run'ın perf DÖNÜŞÜMÜNÜN girdileri: koşu modu + Ayarlar'ın "Resolve cycles at full
    // priority" anahtarı. Run başında, ilk ApplyPerfLocked'tan ÖNCE aynı kilit altında komuttan yazılır; canlı
    // setPerfMode da onları okur (bkz. ApplyPerfLocked). _perfApplied false iken hiçbir yol okumaz, bu yüzden run
    // sonunda ayrıca sıfırlanmaz — her run başı onları yeniden yazar.
    private RunMode _perfRunMode;
    private bool _perfResolveAtFullPriority;
    // [T20-b/P3] Şu an KAÇ worker copy-contention penceresinde. Ref-count zorunludur: paralel build'de birden
    // çok worker aynı anda MSB302x görebilir ve erken çıkan biri, diğeri hâlâ kopyalarken cap'i geri kısardı.
    private int _copyFloorDepth;
    // [T20-b/P3] Graceful drain başladı ⇒ bu run'da cap bir daha YAZILMAZ ve [final review I-1] priority taban
    // değerin altına İNDİRİLEMEZ (pencere kapanışı ve canlı setPerfMode dahil). "torn DLL yok" garantisi,
    // sonradan geri konan bir cap ya da Idle'a düşürülen bir priority ile pazarlık edilemez.
    private bool _capDrained;

    // --- Koşan run'ın state'i (koşu bitince temizlenir; proje logu okuması run boyunca buradan gider) ---
    private RunLogWriter? _logs;
    // [T28] En son (aktif ya da tamamlanmış) run'ın dizini — _logs Dispose edilip null'landıktan SONRA da
    // hayatta kalır: run tamamen bitmiş olsa bile bir proje kartına tıklamak logunu diskten okuyabilsin diye.
    private string? _lastRunDirectory;

    private bool _disposed;

    /// <summary>Aktif (ya da en son) run'ın task'ı: run'ın TÜM event'leri yazıldıktan sonra tamamlanır.</summary>
    public Task RunCompletion { get { lock (_gate) return _runTask; } }

    /// <summary>
    /// [T28] <c>getProjectLog</c>'un tek kaynağı. Aktif koşu varsa (ya da runStarted'a varmadan dönmüş bir
    /// koşunun hâlâ açık writer'ı varsa) canlı writer'dan (in-memory sayaçla
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
    /// SONRA <c>_logs.Dispose()</c> çağrılsa bile (bkz. PlanAndRunAsync'in finally'si, dispose HER ZAMAN
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
    /// ve <c>stopRun</c> asla ulaşamazdı. Reddedilen istekler (<c>runInProgress</c>)
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
            else
            {
                // Slot, arka plan task'ı başlamadan ÖNCE burada tutulur: ikinci bir startRun (planlama sürerken
                // bile) runInProgress alır.
                _runActive = true;
                _finishing = false;
                _stopKind = null;
                _stopAcked = false;
                _interrupted = false;
                _runTask = Task.Run(() => ExecuteRunAsync(cmd, ct), CancellationToken.None);
            }
        }
        if (rejection is not null) await writer.WriteAsync(rejection, ct);
    }

    /// <summary>
    /// [clean/optimize] Salt-okur sonda: bir koşu slotu dolu mu (planlama penceresi DAHİL). Tüketicileri
    /// <c>cleanWorkspace</c> ve <c>optimizeWorkspace</c>'in reddetme kapılarıdır — ikisi de diski değiştirir ve
    /// uçuştaki bir build'in yazdığı klasörlerle yarışmamalıdır.
    /// <para><c>_finishing</c> KASITLI olarak dışarıda: drain sırasında da (sonuç event'leri yazılırken,
    /// in-flight <c>MSBuild.exe</c>'ler post-build copy'lerini bitirirken) reddetmek doğru davranıştır.</para>
    /// </summary>
    public bool IsRunActive { get { lock (_gate) return _runActive; } }

    /// <summary>
    /// [PERF Faz C/C3] Motorun bellek tanı satırını konsol (stderr) kanalına yazar: Sync bitişinde host, koşu
    /// bitişinde koordinatörün kendisi çağırır — kanalın sahibi koordinatördür, host ikinci bir stderr bağı kurmaz.
    /// Hata fırlatmaz (<c>MemoryLine.Report</c> yutar).
    /// </summary>
    public void ReportMemory() => MemoryLine.Report(console);

    /// <summary>
    /// [I2-K1] Aktif run'ın Stop'unu sahiplenir. <c>true</c> → <c>runStopped</c>'ı (in-flight sonuçları
    /// raporlandıktan SONRA) bu koordinatör yazar. <c>false</c> → sahiplenilecek run yok; çağıran (host) kendi
    /// ack'ini vermelidir.
    ///
    /// <para><b>Graceful:</b> <see cref="ReadySetScheduler.RequestStop"/> — yeni dispatch yok, in-flight
    /// <c>MSBuild.exe</c> child'ları post-build copy DAHİL kendi tamamlanmalarını yapar (ortak çıktı dizini
    /// yarım yazılmış kalmaz); [P3] bu drain penceresi boyunca CPU cap KALDIRILIR (bkz.
    /// <see cref="DrainCapLocked"/>). <b>Hard:</b> inner Job ANINDA terminate edilir; in-flight projeler
    /// <c>projectFailed("stopped")</c> raporlanır. <b>Interrupt</b> (branch değişti): Graceful'un kendisi + koşu
    /// kesilmiş sayılır — bundan sonra biten sonuçlar deftere yazılmaz (bkz. <see cref="ReportProjectResult"/>).
    /// Terminate edilmiş Job yeni process kabul ettiği için ikisinden sonra da bir sonraki koşu aynı inner
    /// job'da başlayabilir.</para>
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
                // Interrupt, _stopKind'a Graceful olarak girer (dispatch ve drain kuralı aynı); farkı _interrupted'dadır.
                _stopKind = kind == StopKind.Hard ? StopKind.Hard : _stopKind ?? StopKind.Graceful; // Hard geri alınmaz
                if (kind == StopKind.Interrupt) _interrupted = true;
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
    /// priority'dir</b>: worker'lar run başında bir kez yaratılır (bkz. <see cref="PlanAndRunAsync"/>'in worker
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
        // [RESOLVE Faz 4 / karar 11] Run başı da canlı setPerfMode da profili AYNI Core dönüşümünden geçirir: Resolve
        // cycles koşusunda (anahtar açıkken) yürürlükteki profil cap'siz + Normal'dir; işçi sayısı zaten run başında
        // sabitlenmiştir. Copy penceresi ve drain kuralları dönüşmüş profile uygulanır — cap'siz profilde pencere hiç
        // açılmaz (EnterCopyFloor), priority tabanı Normal'i aşağı çekmez (EffectivePriorityLocked yalnız yükseltir).
        var active = PerfProfile.ForRun(_perfRunMode, profile, _perfResolveAtFullPriority);
        _activePerf = active; // [P3] copy penceresi kapanınca buraya dönülür
        return WritePerfLocked(active, warnings);
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
            // [Task 10 fix I2] Açılış kurtarması defter yazımında patladıysa PLANLAMADAN önce yeniden denenir —
            // kesilmiş projenin kaydı geçersizlenmeden planlanırsa yarım çıktısı "güncel" sayılabilirdi.
            if (stateStore is not null) TrackInFlight(ledger => ledger.RetryRecovery(stateStore, DateTimeOffset.UtcNow));
            await PlanAndRunAsync(cmd, events.Writer, ct);
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
            // SIZMAZ. Bu noktada tüm worker'lar zaten join olmuştur (PlanAndRunAsync döndü), yani kısılacak bir
            // MSBuild child'ı kalmamıştır.
            ReleasePerf();
            // [spec 2026-09-18 §5.5] Uçuş defteri HER çıkışta boşalır (normal, stop, planFailed, beklenmeyen hata):
            // worker'lar join oldu, uçuşta kimse yok — kalan bir satır bir sonraki açılışta boşuna geçersizlerdi.
            TrackInFlight(ledger => ledger.Clear());
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
                _interrupted = false;
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
            // [PERF Faz C/C3] Koşu bitti ve yuva bırakıldı: bellek tanı satırı. Yuvadan SONRA yazılır — tanı hiçbir
            // koşulda yuvayı tutmaz; normal çıkışların hepsi (tamamlanma, Stop, planFailed, beklenmeyen hata) tek satır bırakır.
            ReportMemory();
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

    private async Task PlanAndRunAsync(StartRunCommand cmd, ChannelWriter<IpcEvent> events, CancellationToken ct)
    {
        RunPlan runPlan;
        RunLogWriter logs;
        RunClock clock;
        // [cycle rounds] Scheduler'ın TOHUMU — koşu başında karara bağlanmış pre-skip'ler: Build'de "up to date"
        // (grup düzeyinde güncel SCC dahil); Cycles'ta ayrıca kapsam dışı (OutOfCycleScope). Rebuild/Clean'de BOŞ kalır. Scheduler'ın KENDİSİ aşağıda, dalların DIŞINDA tek bir yerde kurulur
        // (kopya YASAK).
        var schedulerSeed = new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase);
        ConcurrentDictionary<string, IReadOnlyList<string>> depIssuesById;
        // [Task 19] Build VE Cycles modlarında construction anında Skipped sayılan pre-skip'ler (cycle pre-skip'i
        // gibi dependent'ları için resolved) — incremental "up to date" (WillBuild==false) projeler ve grup düzeyinde
        // güncel SCC üyeleri (SkipReasons.UpToDate); Cycles'ta AYRICA kapsam dışı projeler (SkipReasons.OutOfCycleScope).
        // ProjectSkippedEvent ile raporlanır. Rebuild'de boş kalır.
        // [cycle rounds/Task 8] CycleUnconverged BURADA (tipli üçüncü alan) taşınır: App'e giden ayırt edici bayrak
        // Reason METNİNDEN çıkarılmaz (kopya YASAK), doğrudan bu tuple alanından DecideSkipped'e taşınır. Onu true
        // yapan tek kaynak yakınsamama hafızasının SCC pre-skip'iydi; o kalktı (bkz. Cycles tohumundaki
        // [Task 7 · DEĞİŞEN KURAL]) — listeye bugün yalnız "up to date" ve Cycles'ın kapsam dışı skip'leri düşer
        // ve alan hep false taşınır.
        var upToDateSkips = new List<(string ProjectId, string Reason, bool CycleUnconverged)>();
        // [tek proje] Hedefin bu koşuda DERLENMEYEN bayat bağımlılıkları (yalnız kapsamlı koşuda dolu) —
        // dispatch'te dep-issue hesabına girer (bkz. ComputeDepIssues).
        IReadOnlyDictionary<string, IReadOnlyList<StaleDependency>>? staleDependenciesById = null;
        Dictionary<string, string> nameById;
        CycleGroups? groups; // [cycle rounds] SCC üyelik haritasının TEK örneği — plan'ın son hâlinden kurulur (aşağıda)

        {
            // Planlama hataları (I/O, harici hazırlık) ayrı bir kanal AÇMAZ: mevcut planlama-hatası kodu
            // (planFailed) kullanılır — App'in RunEndingErrorCodes kümesi onu zaten tanır (mesaj kullanıcıya
            // gösterilir, Build butonu geri açılır).
            // [planlama görünürlüğü] Planlayıcının adım satırları AYNI FIFO kanaldan gider: sıra korunur,
            // yani hepsi aşağıdaki runStarted'dan ÖNCE App'e ulaşır. TryWrite unbounded kanalda hiç bloklamaz —
            // planlayıcı senkron çalıştığı için bu şarttır.
            try { runPlan = planner(cmd, line => events.TryWrite(new PlanProgressEvent(line))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                or ExternalPreparationException)
            { events.TryWrite(new ErrorEvent("planFailed", ex.Message)); return; }

            // [tek proje · design v1.11.0 §3.8] Satırdan tetiklenen koşu: plan TEK düğüme iner — bağımlılıklar
            // derlenmez, kapsam dışı projeler koşuya hiç girmez. Planlama yine TAM yapıldı (kapsam kararı
            // yukarıdaki planın WillBuild'lerinden okunur; hedefin imzası da o plandan gelir). Planda olmayan
            // bir hedef (bayat topoloji) koşuyu HİÇ başlatmaz — mevcut planlama-hatası kanalıyla.
            // [koşullu yeniden derleme] Önizlemenin kök ADLARI TAM plandan çözülür: kapsamlı koşuda düğüm haritası
            // yalnız hedefi taşır ve kök adları dosya adına düşerdi.
            nameById = runPlan.Plan.Nodes.ToDictionary(n => n.Id, n => n.Name, StringComparer.OrdinalIgnoreCase);
            // [Clean] Clean'in bağımlılık anlamı yoktur: plan kenarsız ve döngüsüz kurulur (gerekçe CleanRunScope'ta).
            // Kapsamdan ÖNCE uygulanır — satır Clean'i de aynı kuraldan geçer, hedefin bayat bağımlılık listesi boş çıkar.
            if (cmd.Mode == RunMode.Clean) runPlan = runPlan with { Plan = CleanRunScope.Of(runPlan.Plan) };
            if (cmd.ScopeProjectId is { } scopeId)
            {
                var scope = ProjectRunScope.Of(runPlan.Plan, scopeId);
                if (scope is null)
                { events.TryWrite(new ErrorEvent("planFailed", ProjectRunScope.NotInPlanMessage(scopeId))); return; }
                runPlan = runPlan with { Plan = scope.Plan };
                staleDependenciesById = new Dictionary<string, IReadOnlyList<StaleDependency>>(StringComparer.OrdinalIgnoreCase)
                { [scope.Target.Id] = scope.StaleDependencies };
            }

            // [cycle rounds] SCC üyelik haritası TEK yerde kurulur ve HEM scheduler'a (grup dispatch'i) HEM run
            // context'ine (tur döngüsü) AYNI örnek verilir — ikisi ayrı From çağrısıyla kurulsaydı üye sırası
            // sessizce ayrışabilirdi. Plan'ın SON hâlinden (Clean ve kapsam daraltmasından sonra) kurulur; tohum
            // bölümündeki retry satırı da grubun adını bu örnekten okur.
            // [Build cycle derler] Harita, SCC derleyen her modda ve yalnız TAM koşuda kurulur — kural tek yerde
            // (CycleCompilation.GroupsFor; Sync'in "bir sonraki Build" önizlemesi de oradan alır): satırdan tetiklenen
            // tek-proje kapsamı hedefi düz düğüm olarak tek başına derler (ProjectRunScope), orada grup yoktur. Plan'da
            // hiç SCC yoksa null geçilir; scheduler o zaman InCycle düğümü "in dependency cycle" ile pre-skip eder —
            // üretimde bu dala düşen düğüm yoktur (planda SCC yoksa InCycle düğüm de yoktur), dal kill-switch testlerinin
            // yoludur. Modlar için yazılmış ayrı bir kod yolu yoktur: Cycles ile Build arasındaki tek fark aşağıdaki
            // KAPSAM tohumudur, Rebuild'inki tur 1'in ve koşullu değerlendirmenin kararıdır.
            groups = CycleCompilation.GroupsFor(runPlan.Plan, cmd.Mode, scopedRun: cmd.ScopeProjectId is not null);

            lock (_gate)
            {
                _logs?.Dispose(); // runStarted'a varmadan dönmüş önceki koşunun (msbuildNotFound ya da beklenmeyen hata) kapanmamış writer'ı — finally'si hiç koşmadı
                logs = _logs = logFactory(DateTimeOffset.Now);
                _lastRunDirectory = logs.RunDirectory;
                depIssuesById = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase); // [T54] taze run → taze birikim
            }
            // [tek proje] decision.log kapsamı adıyla anar: "neden tek proje derlendi" sorusu diskten okunur.
            if (staleDependenciesById is not null)
                Decide(logs, $"scope: single project {runPlan.Plan.Nodes[0].Name} — dependencies are not rebuilt; stale ones are referenced as last known outputs");
            // [Task 19] Build modunda: planlayıcının hesapladığı WillBuild==false projeler "up to date" olarak
            // pre-skip edilir — scheduler'a Skipped tohumlanır (dependent'ları için resolved), dispatch
            // edilmezler. Rebuild HER ŞEYİ derler (tohum yok).
            //
            // [cycles] Cycles modunda KAPSAM daralır: iş yalnız SCC'ler VE onların transitif upstream'idir
            // (gerekçe CycleRunScope'ta) — kapsam dışı her proje koşulsuz pre-skip edilir. Kapsam İÇİNDEKİLER
            // sıradan incremental kurala tabidir.
            // [Build cycle derler] SCC'ler Build'de de Cycles'ta da AYNI grup kapısından geçer — grup olarak güncelse
            // tohumlanır, değilse tek iş kalemi olarak dispatch edilip turlarla derlenir. (Daha önce aynı bileşik
            // imzada yakınsamamış olmak eskiden ikinci bir kapıydı; bugün yalnız raporlanır — aşağıdaki
            // [Task 7 · DEĞİŞEN KURAL].)
            bool cyclesRun = cmd.Mode == RunMode.Cycles;
            var cycleScope = cyclesRun ? CycleRunScope.Of(runPlan.Plan) : null;
            // [Task 7 · DEĞİŞEN KURAL] Yakınsamama hafızası artık BLOKLAMAZ, yalnız RAPORLAR — grubu derleyen her
            // koşuda (harita kuruluysa: Build, Rebuild, Cycles).
            //
            // Eskiden: daha önce yakınsamamış bir SCC, bileşik imzası hâlâ o andakiyle eşleşiyorsa TÜM
            // üyeleriyle pre-skip edilirdi (CycleNonConvergent) — grup hiç dispatch edilmeden. Amaç,
            // kaynak değişmeden aynı sonucu üretecek 2-3 turu boşa harcamamaktı.
            //
            // Neden kalktı: bu kapıya giden TEK yol kullanıcının bir koşu düğmesine (Build, Rebuild ya da
            // Resolve cycles) BASMASIDIR — turları kendiliğinden harcayan otomatik bir akış yok. Yani kapı,
            // tasarrufu yalnız AÇIK bir komutu sessizce yutarak sağlıyordu: düğme hiçbir şey yapmıyor gibi
            // görünüyordu. Aynı gerekçe kod tabanında zaten yazılı — CapReached bilerek HATIRLANMAZ, çünkü
            // "pre-skip edilen bir grupta devam HİÇ gelmez" (bkz. UpdateCycleNonConvergenceMemory). Ayrıca imza
            // yalnız KAYNAKLARI kapsar: paket restore'u, döngü dışı bir bağımlılığın çıktısı ya da ortam
            // değişmiş olabilir — değişmemiş bir kaynak imzasına bakıp yeniden denemeyi reddetmek fazla
            // iddialıdır. Açık basış bir komuttur: grup taze bir çözüme, tur 1'den girer.
            //
            // Hafıza YAZILMAYA devam eder (kanıt) ve burada OKUNUR: operatör grubun neden yine turlar
            // harcadığını decision.log'un ilk satırlarından görür.
            if (groups is not null && stateStore is not null && runPlan.Incremental is { } inc)
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
                    // [Fix round 1 — I1] Grup, başlık ve karar satırlarıyla AYNI adla anılır (CycleGroupName:
                    // build-order lideri). Build-order üyeleri scheduler'ın da okuduğu TEK örnekten (groups), ad
                    // tam plandan kurulmuş nameById'den gelir — geri dönüşü NameOf'unkiyle aynı (kimlik).
                    Decide(logs, CycleDecisionLines.Retrying(
                        CycleGroupName(groups.MembersOf(representative), id => nameById.GetValueOrDefault(id, id)),
                        signature));
                }
            }
            // Güncel tohumu yalnız defteri dinleyen koşularda kurulur (Build, Cycles) — ConditionalRebuild'in mod kuralıyla
            // AYNI kaynak (IncrementalModes, kopya YASAK); Rebuild önbelleği yok sayar.
            if (IncrementalModes.Includes(cmd.Mode))
            {
                // Grup düzeyinde "güncel" bulunan SCC üyeleri — aşağıdaki tek pre-skip döngüsünün cycle
                // üyelerine açtığı KAPIDIR. Harita yoksa (planda SCC yok) boş kalır.
                var cycleUpToDate = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (groups is not null)
                {
                    // SCC'ler de incremental olur: grubu derleyen koşuda planlayıcı üyelere GERÇEK bir WillBuild
                    // verir (bileşik imza — tüm üyeler için ORTAK), dolayısıyla "hepsi false" ⇒ grup gerçekten
                    // güncel demektir ve yeniden derlenmemelidir. Karar GRUP düzeyindedir (All): kısmi bir durumda
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
                    // KAPSAM kapısı yalnız Cycles'ındır: kapsam dışı kalan BURADA tohumlanır ve kendi gerekçesiyle
                    // raporlanır. Build'in kapsamı tüm plandır.
                    if (cyclesRun && !cycleScope!.Contains(n.Id))
                    {
                        schedulerSeed[n.Id] = BuildResult.Skipped;
                        upToDateSkips.Add((n.Id, SkipReasons.OutOfCycleScope, CycleUnconverged: false));
                        continue;
                    }
                    // Cycle üyesi buraya YALNIZ grup kapısından geçtiyse gelir: tekil WillBuild bir SCC üyesini
                    // TEK BAŞINA temsil etmez (bileşik imza gruba aittir). Harita yokken (planda SCC yok — üretimde
                    // InCycle düğüm de yoktur; kill-switch testleri) kapı boştur ve üye tohumlanmaz: ReadySetScheduler
                    // onu kendi "in dependency cycle" gerekçesiyle pre-skip eder, tohumlamak o gerekçeyi yutardı.
                    // Kapsamdaki upstream sıradan projedir ve bu kapıya hiç uğramaz — kendi WillBuild'i onu temsil eder.
                    if (n.InCycle && !cycleUpToDate.Contains(n.Id)) continue;
                    if (n.WillBuild != false) continue;
                    schedulerSeed[n.Id] = BuildResult.Skipped;
                    upToDateSkips.Add((n.Id, SkipReasons.UpToDate, CycleUnconverged: false));
                }
            }
            clock = new RunClock(nowMs);
        }

        // [cycle rounds] groups: planlama bloğunda plan'ın son hâlinden kurulan TEK örnek — scheduler ve run context
        // AYNI örneği alır (gerekçe kuruluş yerinde).
        var scheduler = new ReadySetScheduler(runPlan.Plan, schedulerSeed, groups);

        MsBuildToolset toolset;
        try { toolset = await msbuildFactory(ct); }
        catch (MsBuildResolveException ex)
        { events.TryWrite(new ErrorEvent("msbuildNotFound", ex.Message)); return; }

        // [koşu başı uyarıları görünür] Koşu başı uyarıları runStarted yazılmadan ÖNCE TEK listede toplanır: önce bayat obj
        // satırları, sonra ters katman satırları. Her satır burada decision.log'a yazılır ve AYNI metinle runStarted'la
        // (Warnings) App'e gider; kullanıcının konsol satırını ve event stream'in Warn satırını App yazar (tek projelik
        // koşuda da). stderr'e (console) kopya YAZILMAZ: App stderr'i atar — işçi kırpma ve Resolve notlarıyla aynı
        // sahiplik; stderr ileride yüzeye çıkarsa satır çiftlenmesin.
        // [T72/Task 14] SPIKE S2 — bayat-obj (yabancı-TFM restore artığı) teşhisi her koşuda tetiklenir: her proje kendi
        // varsayılan obj'inde derlenir. Dokunmaz, yalnız warn (StaleObjRunStartWarner ASLA fırlatmaz).
        var runStartWarnings = new List<string>();
        StaleObjRunStartWarner.WarnStaleObj(runPlan.Plan.Nodes, runStartWarnings.Add);
        // [A1/T15] Katman ataması ters-katman bağımlılığı bulduysa (warn-only DATA — koordinatör bunları okuyup
        // bloklama/yeniden sıralama YAPMAZ): LayerEngine'ın ürettiği metin AYNEN, yalnız "warning: " öneki eklenerek.
        // Uyarı kullanıcıya ulaşmazsa, bariyerin bir projeyi kendi bağımlılığından önce koyduğu plan sessizce
        // derlenirdi — tek gerçek düzeltme pattern'leri gözden geçirmektir. Plan katmansızsa (varsayılan)
        // LayerWarnings null/boştur → satır yok.
        foreach (string layerWarning in runPlan.Plan.LayerWarnings ?? [])
            runStartWarnings.Add("warning: " + layerWarning);
        foreach (string runStartWarning in runStartWarnings)
            Decide(logs, runStartWarning);

        // [PERF Faz D / karar 10] Profilin işçi sayısı İSTENEN sayıdır; fiili sayı koşu başında makineye göre BİR KEZ
        // kırpılır (kural ve sabitler Core'da, WorkerBudget — burada yalnız uygulanır). runStarted, konsol başlığı,
        // decision.log ve App'in akış satırı/ETA'sı hep bu FİİLİ sayıyı okur, komuttakini değil. Kırpma olduysa satır
        // (tek sahip: PerfNoteText.WorkersReduced) burada decision.log'a yazılır; gerekçe runStarted'la
        // (WorkersReducedReason) App'e gider ve kullanıcının konsol + event stream satırını App AYNI metinle yazar
        // (tek projelik koşu hariç — orada işçi sayısı koşuyu tarif etmez).
        // [kırpma notu görünür] stderr'e (console) kopya YAZILMAZ: App stderr'i atar — Resolve notuyla aynı sahiplik;
        // stderr ileride yüzeye çıkarsa satır çiftlenmesin.
        var (machineCores, machineFreeBytes) = _machine();
        var workerBudget = WorkerBudget.Clamp(cmd.Parallelism, machineCores, machineFreeBytes);
        int parallelism = workerBudget.Workers;
        if (workerBudget.Reason is { } reductionReason)
            Decide(logs, PerfNoteText.WorkersReduced(parallelism, reductionReason));
        // [T20-b/K11] Perf profili: PARALELLİK BURADAN GELMEZ. Buradan yalnız CPU cap + priority alınır; işçi sayısı
        // yukarıdaki bütçeden (WorkerBudget) gelir: komutun İSTEDİĞİ sayı, makineye göre kırpılmış hâliyle.
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
            _perfRunMode = cmd.Mode;
            _perfResolveAtFullPriority = cmd.ResolveAtFullPriority;
            if (perf is { } profile) { _perfApplied = true; appliedCap = ApplyPerfLocked(profile, perfWarnings); }
            if (_stopKind is not null) scheduler.RequestStop(); // plan kurulurken gelmiş Stop
        }
        Warn(perfWarnings);

        clock.Start();
        var plan = runPlan.Plan;
        var nodeById = plan.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        events.TryWrite(new RunStartedEvent(cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism,
            plan.Configuration, appliedCap, LogDirectory: logs.RunDirectory, WorkersReducedReason: workerBudget.Reason,
            Warnings: runStartWarnings.Count > 0 ? runStartWarnings : null));
        // [Task 17] runStarted'dan HEMEN SONRA, ilk projectStarted/projectSkipped'ten ÖNCE: App'in Projects
        // listesini will-build önizlemesiyle pre-populate edebilmesi için. WillBuild alanı doğrudan plan'ın
        // düğümlerinden (BuildPreview/IncrementalPlanner'ın doldurduğu — henüz run akışına tam bağlanmadıysa null)
        // taşınır; burada AYRICA hesaplanmaz.
        // [W1] BuiltCommit (sha çiftinin sol yarısı) da BURADAN taşınır — Sync'te doldurup burada boş bırakmak,
        // run başlar başlamaz kartların sha slotunu sıfırlardı. Load() ITEM BAŞINA DEĞİL, TOPLU okunur —
        // önizlemenin tamamı tek okumadan beslenir. Yukarıdaki [Task 7] yakınsamama hafızası taraması store'u
        // ayrıca okur; o yalnız grup haritası kurulan koşuda (Build, Rebuild, Cycles) koşar ve ayrı bir soruyu cevaplar.
        var builtCommits = stateStore?.Load();
        // Önizleme BU KOŞUNUN yapacağını anlatır, planlayıcının soyut "dirty mi" cevabını değil: pre-skip
        // edilmiş her proje WillBuild=false gösterilir. İki yer arasındaki fark aksi halde kullanıcıya YALAN
        // söylerdi — amber "derlenecek" noktası, hemen ardından "skipped" olarak geçen bir satırda. Tohum
        // "up to date" skip'lerinde zaten WillBuild==false'tan doğar, orada bu projeksiyon no-op'tur; ayrıştığı
        // tek yer, gerekçesi imzadan DEĞİL koşu kapsamından gelen skip'lerdir: Cycles modunun kapsam dışı
        // bıraktığı projeler. (Yakınsamama hafızası eskiden ikinci bir kaynaktı; artık pre-skip etmiyor — bkz.
        // Cycles tohumundaki [Task 7 · DEĞİŞEN KURAL].)
        var preSkipped = upToDateSkips.Select(s => s.ProjectId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        // [Faz 3/Task 6] Planın kararına giren çıktı kontrolü (Program.ComputeIncremental) — yoksa kanıtsız.
        OutputCheck? CheckOf(string id) => runPlan.Incremental?.ChecksById?.GetValueOrDefault(id);
        // [koşullu yeniden derleme] Bu koşunun sırası geldiğinde koşullu değerlendireceği projeler — karar Core'da
        // (ConditionalRebuild.ConditionalIds; Sync'in önizlemesi de AYNI fonksiyonu AYNI grup haritasıyla sorar); pre-skip
        // edilen hiçbir proje dispatch edilmediği için koşullu da değildir. Önizleme ve dispatch AYNI kümeyi okur:
        // "kuyrukta değil" diyen önizleme ile atlayan motor ayrışamaz.
        var conditionalIds = ConditionalRebuild.ConditionalIds(plan.Nodes, cmd.Mode,
            scopedRun: cmd.ScopeProjectId is not null, groups, preSkipped);
        events.TryWrite(new BuildPreviewEvent(
            // [DEĞİŞEN KURAL — v1.16.0] Pre-skip edilen satırın gerekçesi artık DÜŞMEZ. Eskiden null'lanırdı
            // ("bu false imzadan değil koşu-zamanlama kuralından geliyor, imza gerekçesini göstermek yalan
            // olur") — o dönemde gerekçe bir PLAN kanalını besliyordu. Artık satırın KARAR ETİKETİNİ besliyor
            // ve etiket bir disk olgusudur: yakınsamayan ya da kapsam dışı bir projenin dosyalarının değişip
            // değişmediği doğru bir bilgidir. WillBuild yine false kalır — bu koşu onu derlemez.
            [.. plan.Nodes.Select(n => new BuildPreviewItem(n.Id, n.Name,
                preSkipped.Contains(n.Id) ? false : n.WillBuild,
                BuildStateStore.BuiltCommitOf(builtCommits, n.Id), n.WillBuildReason,
                // [Faz 3/Task 6] modified ↔ affected ve "built outside" yaşı Sync ile AYNI yardımcılardan: zaman
                // kipinde kanıttan, diğer kiplerde defterin cevabı (kontrol yoksa bugünkü cevap aynen).
                OwnFilesChanged: OutputEvidence.OwnFilesChanged(
                    CheckOf(n.Id), builtCommits, n.Id, runPlan.Incremental?.ContentById?.GetValueOrDefault(n.Id)),
                LastBuiltAt: BuildStateStore.LastBuiltAtOf(builtCommits, n.Id),
                Conditional: conditionalIds.Contains(n.Id),
                DependencyRoots: ConditionalRebuild.RootNames(n.WillBuildReason,
                    builtCommits?.GetValueOrDefault(n.Id), id => nameById.GetValueOrDefault(id)),
                // [Task 3] FailedAt AYNI yardımcıdan (BuildStateStore.FailedAtOf) taşınır — Sync ve run yolu
                // aynı aramayı iki kez YAZMAZ (BuiltCommit/LastBuiltAt ile aynı desen). LocalEdits burada
                // TAŞINMAZ (default false): o Sync'in "o anki çalışma ağacı" işaretidir, bir koşunun kendi
                // önizlemesi bunu yeniden hesaplamaz — etiket Sync'ten gelen değeri korur.
                FailedAt: BuildStateStore.FailedAtOf(builtCommits, n.Id),
                OutputBuiltAt: OutputEvidence.OutputBuiltAt(CheckOf(n.Id), n.WillBuildReason)))]));
        // [koşu başı uyarıları görünür] Ters katman uyarıları burada konsola BASILMAZ: runStarted'dan önce bayat obj
        // satırlarıyla aynı listede toplanıp (StaleObjRunStartWarner çağrısının yanında) kullanıcıya App üzerinden ulaşır.
        // Belirsiz üretici uyarıları metinlerini KENDİLERİ taşır (PlanProgressLines "warning: " ile başlar) —
        // Sync transkriptindeki satırın AYNISI; burada ikinci bir önek eklenmez.
        foreach (string warning in plan.ProducerWarnings ?? [])
            console(warning);
        // v7Δ-7: konsolda solution-level msbuild izlenimi verilmez — motorun gerçeği proje-başına shell-out'tur,
        // gerçek komut satırları proje loglarındadır.
        console(string.Format(CultureInfo.InvariantCulture,
            // [D1 review · A3] console(...) Supervisor'ın stderr tanı kanalıdır (Program.cs); App onu okuyup ATAR
            // (EngineHost, ARCHITECTURE §4.3) — kullanıcıya görünen satırlar IPC olaylarıyla gider. Metin yine
            // İngilizce: log/tanı dili.
            "Run {0} ({1}): {2} projects, {3} workers, {4}, {5} — each project is built as its own compiler child process; command lines are in the project logs.",
            cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism, plan.Configuration,
            perf is null ? PerfNoteText.CapTextUnset : PerfNoteText.CapText(appliedCap)));
        Decide(logs, string.Format(CultureInfo.InvariantCulture,
            "run {0} started: mode={1} projects={2} parallelism={3} configuration={4} cpuCap={5}",
            cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism, plan.Configuration,
            perf is null ? PerfNoteText.CapValueUnset : PerfNoteText.CapValue(appliedCap)));
        // [RESOLVE Faz 4 / karar 11] Resolve cycles profilin önceliği/tavanı yerine tam öncelikte koşuyorsa bunu run
        // başında söyleyen TEK satır (metin PerfNoteText'te, chip notunun ailesinde; işçi sayısı yukarıdaki bütçeden) —
        // decision.log'a. Dönüşüm profili değiştirmediyse satır yoktur. [fix 1A — I1] Kullanıcının konsolundaki satırı App
        // yazar (runStarted'ta, AYNI metin); stderr'e kopya YAZILMAZ — App stderr'i atar, ileride konsola taşınırsa satır
        // çiftlenmesin.
        if (perf is { } chosenPerf
            && PerfNoteText.ResolveNote(cmd.Mode, chosenPerf with { Parallelism = parallelism }, cmd.ResolveAtFullPriority)
                is { } resolveNote)
            Decide(logs, resolveNote);

        // runStarted yazıldı: buradan SONRA hangi yoldan çıkılırsa çıkılsın (beklenmeyen exception dahil)
        // kapanış olayları TAM OLARAK BİR KEZ yazılır — aksi halde App'in run'ı sonsuza dek "koşuyor" kalırdı.
        try
        {
            // [A13/B2] Skip satırının metni TEK yerde: iki döngü de aynı cümleyi kuruyordu, çeviri sonrası
            // birebir aynı literal iki kez yaşardı (kopya YASAK, CLAUDE.md).
            // [cycle rounds/Task 8] cycleUnconverged TİPLİ bir parametredir, Reason'dan ÇIKARILMAZ — çağıran değeri
            // kendi listesinden bilir, burada yalnız ProjectSkippedEvent'e AYNEN taşınır. Bugün iki çağıranın ikisi
            // de false verir: onu true yapan yakınsamama hafızası pre-skip'i kalktı (bkz. Cycles tohumundaki
            // [Task 7 · DEĞİŞEN KURAL]).
            void DecideSkipped(string projectId, string reason, bool cycleUnconverged = false) =>
                ReportSkipped(events, logs, cmd.RunId, projectId, nodeById[projectId].Name, reason, cycleUnconverged);

            // Cycle üyeleri (construction anında Skipped) — PreSkipped yalnız grup haritası OLMAYAN koşuda dolar
            // ("in dependency cycle"): planda SCC yokken (üretimde orada InCycle düğüm de yoktur) ve kill-switch
            // testlerinde. Build, Rebuild ve Cycles'ta boştur (gruplar turlarla derlenir — BuildCycleGroupAsync),
            // Clean'de boştur (plan döngü işaretsiz, CleanRunScope). Yakınsamama hafızasıyla İLGİSİZDİR,
            // cycleUnconverged varsayılan false kalır.
            foreach (var (projectId, reason) in scheduler.PreSkipped)
                DecideSkipped(projectId, reason);
            // [Task 19] Build VE Cycles modlarının pre-skip'leri (cycle pre-skip ile AYNI konumda, ilk
            // dispatch'ten ÖNCE) — Build'de incremental "up to date", Cycles'ta AYRICA kapsam dışı ve grup
            // düzeyinde güncel SCC üyeleri: dependent'ları için scheduler'da zaten Skipped/resolved tohumlandı.
            foreach (var (projectId, reason, cycleUnconverged) in upToDateSkips)
                DecideSkipped(projectId, reason, cycleUnconverged);

            // [seviyeli turlar] Slot sayısı worker sayısıyla AYNI kaynaktan (kırpılmış FİİLİ sayı, yukarıdaki bütçe) —
            // koşu bitiminde hiçbir bekleyen kalmaz (tüm worker'lar ve seviye görevleri await edilmiş olur), using
            // güvenlidir.
            using var invokeSlots = new SemaphoreSlim(parallelism, parallelism);
            var run = new RunContext(
                cmd.RunId, plan.Configuration, runPlan.SolutionRefs,
                nodeById,
                scheduler, wake, logs, events,
                // Retry politikası Core'un [T8]; burada yalnız run'a bağlanır: onRetry hem decision.log'a hem konsola.
                // [T20-b/P3] cpuFloor: contention penceresinde cap'i tabana yükseltir — cap'in TEK yazıcısı
                // bu koordinatör olduğu için seam de buraya bağlanır (bkz. EnterCopyFloor/ExitCopyFloor).
                new RetryingMsBuildInvoker(toolset.Invoker, RetryingMsBuildInvoker.DefaultBackoff,
                    retryDelay ?? ((delay, token) => Task.Delay(delay, token)),
                    onRetry: message => { Decide(logs, message); console(message); },
                    cpuFloor: new CoordinatorCpuFloor(this)),
                toolset.MsBuildExePath,
                depIssuesById, // [T54]
                stateStore, // [Task 19] projectSucceeded → BuildState persist (null ⇒ persist YOK, mevcut test davranışı)
                runPlan.Incremental, // [Task 19] imza + HEAD + branch (persist için)
                groups, // [cycle rounds] scheduler ile AYNI örnek — dispatch edilen id bir grup üyesi mi
                conditionalIds, // [koşullu yeniden derleme] önizlemenin okuduğu AYNI küme
                builtCommits, // [koşullu yeniden derleme] koşu başındaki defter — derlenmeyen kökün son sonucu
                invokeSlots, // [seviyeli turlar] MSBuild-child tavanı — worker'lar + seviye üyeleri tek kapıdan
                staleDependenciesById, // [tek proje] hedefin derlenmeyen bayat bağımlılıkları (yalnız kapsamlı koşuda)
                // [tek proje · design §3.8] MSBuild hedefi: Clean modu doğrudan -t:Clean koşar (hiçbir şey
                // derlenmez). Rebuild YALNIZ satırdan tetiklendiğinde MSBuild'in kendi Rebuild'i olur — alt
                // bardaki Rebuild "cache'i yok say" demektir ve proje başına yine -t:Build koşar; tek projelik
                // bir kapsamda o anlamı zaten Build taşıdığı için satırdaki Rebuild'in ayrı bir anlamı olmalıdır.
                cmd.Mode switch
                {
                    RunMode.Clean => MsBuildTarget.Clean,
                    RunMode.Rebuild when cmd.ScopeProjectId is not null => MsBuildTarget.Rebuild,
                    _ => MsBuildTarget.Build,
                },
                // [WPF geçici assembly] Motorun açılışta yazdığı targets dosyası: her derleme isteğine taşınır.
                CustomBeforeTargetsPath: toolset.CustomBeforeTargetsPath,
                // [PERF E3] Restore kararı koşunun modunu okur (Rebuild her zaman restore eder).
                Mode: cmd.Mode,
                // [D7] Kapı yalnız defteri dinleyen tam koşuda uygulanır — kural TEK yerde (SurfaceGate.AppliesTo); planın
                // adaylarını üreten Program da aynı fonksiyonu sorar.
                SurfaceCandidateIds: SurfaceGate.AppliesTo(cmd.Mode, scopedRun: cmd.ScopeProjectId is not null)
                    ? runPlan.Incremental?.SurfaceCandidateIds
                    : null);

            var workers = Enumerable.Range(0, parallelism)
                .Select(_ => Task.Run(() => WorkerAsync(run, ct), CancellationToken.None))
                .ToArray();
            try { await Task.WhenAll(workers); }
            catch (Exception ex)
            {
                // Worker'lar normalde fırlatmaz (her proje kendi sonucunu raporlar). Yine de fırlarsa: run ASILI
                // KALMAZ — aşağıdaki finally kalan sayısını okuyup runCompleted yazar; kalanlar Queued olarak raporlanır.
                Decide(logs, "a worker terminated unexpectedly: " + ex.Message);
            }
        }
        finally
        {
            // [Kısıt 4] Kalan sayısı ANCAK tüm worker'lar join olduktan sonra okunur (her in-flight proje
            // sonucunu raporlamıştır) — hem graceful hem hard için. Böylece kalan sayısı kesindir, "öldürüldü"
            // ≠ "raporlandı" belirsizliği yoktur.
            clock.Pause();
            int unfinished = scheduler.UnfinishedCount;

            StopKind? stopKind;
            // _finishing: bundan sonra TryRequestStop sahiplenmez. _stopAcked: runStopped'ı BURADA yazıyoruz,
            // ExecuteRunAsync'in ack-borcu kapatıcısı bir daha yazmasın (tek runStopped garantisi).
            lock (_gate) { stopKind = _stopKind; _finishing = true; if (stopKind is not null) _stopAcked = true; }

            var outcome = stopKind is null ? RunOutcome.Completed : RunOutcome.Stopped;
            var completed = scheduler.Completed;
            int succeeded = completed.Count(kv => kv.Value == BuildResult.Succeeded);
            int failed = completed.Count(kv => kv.Value == BuildResult.Failed);
            int skipped = completed.Count(kv => kv.Value == BuildResult.Skipped);
            // [T54] Bu koşunun dependency-affected proje sayısı — depIssues'u boş OLMAYAN projeler. Kendisi failed
            // bir kök, kendi depIssue'unu taşımaz (sayılmaz).
            // [koşullu yeniden derleme] "dependency still failing" ile atlanan proje de birikime köklerini yazar
            // (dependent'ları miras alsın diye) ama bu koşuda DERLENMEDİ — sayılmaz.
            int depIssueCount = depIssuesById.Count(
                kv => kv.Value.Count > 0 && completed.GetValueOrDefault(kv.Key) != BuildResult.Skipped);

            // Olaylar ÖNCE (TryWrite fırlatmaz), disk logu sonra: log I/O'su patlasa bile App kapanışı görür.
            if (stopKind is not null)
                events.TryWrite(new RunStoppedEvent(cmd.RunId, WasHard: stopKind == StopKind.Hard));
            events.TryWrite(new RunCompletedEvent(cmd.RunId, outcome, succeeded, failed, skipped,
                unfinished, clock.ElapsedMs, depIssueCount));
            Decide(logs, string.Format(CultureInfo.InvariantCulture,
                "run {0} finished: outcome={1} succeeded={2} failed={3} skipped={4} queued={5} duration={6}ms depIssues={7}",
                cmd.RunId, outcome, succeeded, failed, skipped, unfinished, clock.ElapsedMs, depIssueCount));

            // [design v1.7.0 §3.1] Koşu bittiğinde HER ŞEY temizlenir — devredilecek bir segment yoktur.
            // Eskiden Stop/hata sonrası plan/logs/birikimler saklanırdı, çünkü Continue ve RetryFailed AYNI
            // plan üstünden ikinci bir segment koşuyordu. O iki mod kaldırıldı: sonraki Build taze planlar ve
            // ne derleyeceğini persist edilmiş BuildState'ten bulur (öldürülen/başarısız projeler geçersiz,
            // yeşil bitenler güncel).
            // [Kısıt 1] RunLogWriter ancak TÜM worker'lar join olduktan sonra dispose edilir.
            lock (_gate) _logs = null;
            logs.Dispose();
        }
    }

    /// <summary>[design v1.14.0 §9] Bu proje ana repo DIŞINDAKİ bir çalışma alanı kökünden mi geldi —
    /// topolojiden okunur (rozet <see cref="ProjectNode.IsExternal"/>), ayrı bir liste tutulmaz. Tek bir
    /// karar verir: build-state'e yazılan commit/branch.</summary>
    private static bool IsExternal(RunContext run, string projectId) =>
        run.NodeById.GetValueOrDefault(projectId)?.IsExternal == true;

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
            // [dalga görünürlüğü] Slot İŞ İSTENMEDEN önce alınır: tekil proje ancak bir MSBuild slotu tutarken
            // dispatch edilir, yani "derleniyor" ilanı (BuildProjectAsync'teki ProjectStartedEvent) her zaman
            // gerçekten başlayan bir derlemeyi anlatır. Eskiden slot invoke'un hemen önünde bekleniyordu:
            // koşan bir SCC dalgası slotları doldurmuşken dispatch edilen proje sırasını beklerken de
            // "derleniyor" görünürdü (ekranın sayacı paralelliği aşardı) ve beklerken düşen bir Stop'tan SONRA
            // yeni bir MSBuild başlatırdı. Kimse bir slotu tutarken başka bir slot beklemez: kilitlenme yoktur.
            try { await run.InvokeSlots.WaitAsync(ct); }
            catch (OperationCanceledException) { return; } // Supervisor kapanıyor
            if (!run.Scheduler.TryDispatch(out string projectId))
            {
                run.InvokeSlots.Release();
                // [Kısıt 2] TryDispatch==false "run bitti" DEĞİL, "şu an hazır iş yok" demektir — bağımlılıklar
                // hâlâ derleniyor olabilir. Burada dönmek run'ı sessizce kırpardı; bunun yerine park edilir.
                try { await wake.WaitAsync(ct); }
                catch (OperationCanceledException) { return; } // Supervisor kapanıyor
                continue;
            }
            // [cycle rounds] Dispatch edilen id bir SCC üyesiyse scheduler TÜM üyeleri in-flight işaretlemiştir
            // (bkz. ReadySetScheduler.TryDispatch) — o zaman iş kalemi tek bir proje değil, grubun TAMAMIDIR.
            // Grup slotlarını üye başına kendisi alır (seviye üyeleri eşzamanlıdır): dispatch'in slotu hemen
            // havuza döner.
            var members = run.Groups?.MembersOf(projectId) ?? [];
            if (members.Count > 0) run.InvokeSlots.Release();
            try
            {
                if (members.Count == 0) await BuildProjectAsync(run, projectId, ct);
                // [grup koşullu atlama] Tekil projenin §8.3 kuralının grup-atomik hâli: tüm üyeler yalnız
                // köklerini bekliyorsa ve kökler bu koşuda da hâlâ kırıksa grup HİÇ derlenmez — yeniden
                // derlemek herkesi aynı bayat köke yeniden link'lemekten başka bir şey yapmazdı (sahada
                // ölçüldü: kırık bir kök, 17 üyeli grubu her Resolve basışında ~98 sn boşuna derletiyordu).
                else if (!TrySkipGroupWhileDependenciesStillFail(run, members))
                    await BuildCycleGroupAsync(run, members, ct);
            }
            finally
            {
                // Tekil projenin slotu SONUCU yazıldıktan sonra bırakılır (BuildProjectAsync onu kendi
                // finally'sinde raporlar): bir sonraki "derleniyor" ilanı, bu projenin sonucundan önce gelemez.
                if (members.Count == 0) run.InvokeSlots.Release();
                run.Wake.WakeAll(); // Complete edildi (ya da patladı) → parked worker'lar yeniden baksın
            }
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
        // [koşullu yeniden derleme] Sıra geldi: tüm bağımlılıklar (ve üstlerindeki kökler) bu koşuda terminal.
        if (run.ConditionalIds.Contains(projectId) && TrySkipWhileDependencyStillFails(run, projectId)) return;
        // [D7] Yüzey kapısı: aday projenin hiçbir doğrudan bağımlılığının API yüzeyi değişmediyse derlenmez.
        if (run.SurfaceCandidateIds?.Contains(projectId) == true && TrySkipWhileDependencySurfacesUnchanged(run, projectId)) return;

        var result = BuildResult.Failed;
        long durationMs = 0;
        string? failReason = null;
        string? failLogTail = null;

        // [T54] depIssues invoke'tan ÖNCE hesaplanır — gerekçe ComputeDepIssues'ın XML doc'undadır.
        var depIssues = ComputeDepIssues(run, projectId);
        // [D6] Bağımlılık yüzeyleri de invoke'tan ÖNCE: bağımlılıklar şu an terminaldir ve derleme bu yüzeylere karşı
        // yapılır; başarı deftere bunları yazar (yüzey kapısının bir sonraki koşudaki tabanı).
        var dependencySurfaces = DependencySurfacesOf(run, projectId);

        try
        {
            TrackInFlight(ledger => ledger.Add(projectId)); // [§5.5] dispatch anı: motor ölürse açılış bunu geçersizler
            run.Events.TryWrite(new ProjectStartedEvent(run.RunId, projectId, NameOf(run, projectId)));

            InvokeOutcome outcome;
            // [Kısıt 1] Proje logu YALNIZCA bu projenin invoke'u bittikten sonra dispose edilir (dispose
            // sonrası AppendLine fırlatır — satır sessizce düşmez). Ömür BURADA, invoke'un içinde DEĞİL.
            using (var log = run.Logs.OpenProjectLog(projectId))
                outcome = await InvokeOnceAsync(run, projectId, depIssues, log, ct); // slot: WorkerAsync tutuyor

            result = outcome.Result;
            durationMs = outcome.DurationMs;
            failReason = outcome.FailReason;
        }
        catch (OperationCanceledException)
        {
            failReason = FailureReasons.Stopped;
            failLogTail = " (cancelled)"; // decision.log metni korunur: süre değil, iptal edildiği yazılır
        }
        catch (Exception ex)
        {
            // Invoke/log yolunda beklenmeyen hata: proje tek başına düşer, run devam eder ("hata derlemeyi
            // öldürmez", A3) — ve aşağıdaki finally sayesinde scheduler ASLA askıda kalmaz.
            failReason = InvokeErrorReason(ex);
            failLogTail = ""; // reason zaten hatayı taşır; satır sonuna ayrıca süre EKLENMEZ
        }
        finally
        {
            ReportProjectResult(run, projectId, result, durationMs, failReason, depIssues,
                trustedResult: true, cycleUnsettled: false, failLogTail, dependencySurfaces: dependencySurfaces);
        }
    }

    /// <summary>[koşullu yeniden derleme] Bir kökün BU KOŞUNUN önizlemesindeki gerekçesi —
    /// <see cref="ConditionalRebuild"/>'in kök sınıflandırması bunu defterin son sonucundan ÖNCE okur. Karar ve
    /// atlama satırı AYNI kaynağı görür (kopya YASAK).</summary>
    private static Func<string, WillBuildReason?> ReasonOf(RunContext run) =>
        id => run.NodeById.GetValueOrDefault(id)?.WillBuildReason;

    /// <summary>
    /// [koşullu yeniden derleme] Koşullu projenin kararını UYGULAR (karar <see cref="ConditionalRebuild.Decide"/>'da).
    /// Kayıtlı köklerin hepsi hâlâ hatalıysa proje derlenmeden <see cref="SkipReasons.DependencyStillFailing"/> ile
    /// atlanır ve <c>true</c> döner; aksi hâlde <c>false</c> (çağıran normal derler).
    ///
    /// <para><b>Defter kaydına DOKUNULMAZ:</b> not, kökler ve <see cref="BuildState.BuiltSignature"/> olduğu gibi
    /// kalır — proje hâlâ bayat köke link'lidir ve kök düzeldiğinde derlenmesi gerekir. <b>Birikime ise kökler
    /// yazılır:</b> bu projenin çıktısı hâlâ o köklere link'lidir; ona bağlı olup bu koşuda derlenen proje notu
    /// miras almazsa kök düzeldiğinde kendi imzası değişmediği için sonsuza dek bayat kalırdı.</para>
    ///
    /// <para>Karar hesaplanırken beklenmedik bir hata olursa proje DERLENİR (güvenli yön). Atlama yolunda
    /// <see cref="ReadySetScheduler.Complete"/> <c>finally</c>'dedir: dispatch edilmiş proje hiçbir yoldan askıda
    /// kalmaz.</para>
    /// </summary>
    private bool TrySkipWhileDependencyStillFails(RunContext run, string projectId)
    {
        BuildState? recorded;
        try
        {
            recorded = run.LedgerAtStart?.GetValueOrDefault(projectId);
            if (ConditionalRebuild.Decide(recorded?.DepIssueRoots, run.Scheduler.Completed, run.NodeById.ContainsKey,
                    run.LedgerAtStart, ReasonOf(run)) != ConditionalRebuildVerdict.DependencyStillFailing)
                return false;
        }
        catch (Exception ex)
        {
            console("warning: conditional rebuild check failed (" + NameOf(run, projectId) + ") — building: " + ex.Message);
            return false;
        }

        try
        {
            run.DepIssuesById[projectId] = recorded!.DepIssueRoots!;
            // [Task 4 — carried item 3] Kök adları BURADA ("dependency still failing (…)" satırı) yalnız
            // RootNames'in düz listesi DEĞİL, DescribeStillFailingRoots'un KANITLI listesidir: bir kök bu
            // koşuda hiç denenmediyse (atlanmış ya da henüz sonuçlanmamış, önizlemesi güncel değil) ve "hâlâ hatalı" iddiası
            // yalnız koşu başındaki defterden geliyorsa satır bunu söyler — "R failed in this run" YALANI
            // basılmaz. Kök GERÇEKTEN bu koşuda patladıysa (bugünkü senaryoların hepsi) metin DEĞİŞMEZ.
            string roots = string.Join(", ", ConditionalRebuild.DescribeStillFailingRoots(
                recorded!.DepIssueRoots, run.Scheduler.Completed, run.NodeById.ContainsKey, run.LedgerAtStart,
                ReasonOf(run),
                id => run.NodeById.GetValueOrDefault(id)?.Name ?? Path.GetFileNameWithoutExtension(id)));
            ReportSkipped(run.Events, run.Logs, run.RunId, projectId, NameOf(run, projectId),
                SkipReasons.DependencyStillFailing, cycleUnconverged: false, detail: roots);
        }
        finally
        {
            run.Scheduler.Complete(projectId, BuildResult.Skipped);
        }
        return true;
    }

    /// <summary>
    /// [D7] Yüzey kapısını UYGULAR (karar <see cref="SurfaceGate.Decide"/>'da): hiçbir doğrudan bağımlılığın yüzeyi değişmediyse
    /// proje derlenmeden <see cref="SkipReasons.UpToDate"/> + <see cref="SurfaceGate.UnchangedDetail"/> ile atlanır, defteri
    /// yenilenir (<see cref="RefreshBuildStateOnSkip"/> — taşınan döngü üyesiyle AYNI gövde) ve <c>true</c> döner; aksi hâlde
    /// <c>false</c>. Dep-issue'lar atlanan projede de hesaplanır: bağımlılarına miras kalır, nota yazılır. Beklenmedik hata ⇒
    /// derlenir. Complete <c>finally</c>'de.
    /// </summary>
    private bool TrySkipWhileDependencySurfacesUnchanged(RunContext run, string projectId)
    {
        try
        {
            var deps = run.NodeById.GetValueOrDefault(projectId)?.Dependencies ?? [];
            var recorded = run.LedgerAtStart?.GetValueOrDefault(projectId);
            if (SurfaceGate.Decide(deps, run.Scheduler.Completed, recorded, d => SurfaceOf(run, d)) != SurfaceGateVerdict.Unchanged)
                return false;
        }
        catch (Exception ex)
        {
            console("warning: surface gate check failed (" + NameOf(run, projectId) + ") — building: " + ex.Message);
            return false;
        }
        try
        {
            var depIssues = ComputeDepIssues(run, projectId);
            ReportSkipped(run.Events, run.Logs, run.RunId, projectId, NameOf(run, projectId), SkipReasons.UpToDate,
                cycleUnconverged: false, detail: SurfaceGate.UnchangedDetail);
            bool interrupted;
            lock (_gate) interrupted = _interrupted;
            if (!interrupted) RefreshBuildStateOnSkip(run, projectId, depIssues);
        }
        finally
        {
            run.Scheduler.Complete(projectId, BuildResult.Skipped);
        }
        return true;
    }

    /// <summary>[D9] Bir projenin ŞİMDİKİ çıktı yüzeyi: kanıt dosyası (IncrementalPlan.OutputsById) + yüzey özeti; koşu başına
    /// bir okuma (RunContext.SurfaceById). Kanıt yolu türetilemiyorsa null.</summary>
    private (string File, string? Hash)? SurfaceOf(RunContext run, string projectId)
    {
        if (run.Incremental?.OutputsById?.GetValueOrDefault(projectId) is not { } outputs) return null;
        return (outputs.Evidence, run.SurfaceById.GetOrAdd(projectId, _ => _apiSurface(outputs.Evidence)));
    }

    /// <summary>[D6] Bu projenin doğrudan bağımlılıklarının (döngü üyesinde grup DIŞI olanların) şimdiki yüzeyleri, defterin
    /// kanonik listesi olarak; yüzeyi olmayan/okunamayan bağımlılık listeye girmez (SurfaceGate.Persistable). Hiçbiri
    /// listelenemezse (bağımlılık yok, kanıt yok) <c>null</c> — eski kayıtla aynı "yüzey kanıtı yok" hâli; kapı ikisini de
    /// derler. HİÇ FIRLATMAZ (warn-only, null): çağıranlar sonuç raporlamasının hemen yanındadır ve buradan kaçan bir istisna
    /// Complete'i atlatıp koşuyu asardı — üretimdeki ApiSurfaceHash.OfFile zaten fırlatmaz, bu kapı sahte seam'ler ve
    /// gelecekteki yazımlar içindir.</summary>
    private IReadOnlyList<CycleReadSurface>? DependencySurfacesOf(RunContext run, string projectId,
        IReadOnlyCollection<string>? excludedDeps = null)
    {
        try
        {
            var list = new List<CycleReadSurface>();
            foreach (string dep in run.NodeById.GetValueOrDefault(projectId)?.Dependencies ?? [])
            {
                if (excludedDeps is not null && excludedDeps.Contains(dep, StringComparer.OrdinalIgnoreCase)) continue;
                if (SurfaceOf(run, dep) is { } s && SurfaceGate.Persistable(s.Hash) is { } hash)
                    list.Add(new CycleReadSurface(dep, s.File, hash));
            }
            return list.Count == 0 ? null : [.. list.OrderBy(s => s.Producer, StringComparer.OrdinalIgnoreCase)];
        }
        catch (Exception ex)
        {
            console("warning: dependency surfaces could not be read (" + NameOf(run, projectId) + "): " + ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Bir skip'i raporlayan TEK gövde: <see cref="ProjectSkippedEvent"/> + <c>decision.log</c> satırı
    /// (<c>"&lt;ad&gt;: skipped — &lt;gerekçe&gt;"</c>, varsa <paramref name="detail"/> parantez içinde). Koşu başındaki
    /// pre-skip'ler ile sırası gelince atlanan koşullu proje aynı cümleyi kurar (kopya YASAK).
    /// </summary>
    private void ReportSkipped(ChannelWriter<IpcEvent> events, RunLogWriter logs, string runId, string projectId,
        string name, string reason, bool cycleUnconverged, string? detail = null)
    {
        events.TryWrite(new ProjectSkippedEvent(runId, projectId, reason, cycleUnconverged));
        Decide(logs, string.IsNullOrEmpty(detail)
            ? $"{name}: skipped — {reason}"
            : $"{name}: skipped — {reason} ({detail})");
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
    /// <param name="trustedResult">Bu sonucun ARKASINDA DURULABİLİR mi. Tekil projede daima <c>true</c>. SCC'de üye
    /// OTURMUŞSA (grup yakınsadı, ya da yakınsamadı ama üyenin okuduğu hiçbir yüzey son turda bayat değildi) ya da
    /// [suçlu kırmızı] yüzeyleri oturmuşken patlamış kanıtlı-umutsuz hataysa (bkz. <see cref="ReportCycleMember"/>)
    /// <c>true</c>'dur. Bayat yüzeye bağlanmış yeşil üye taze imzasını KAYDETMEZ — aksi halde bir sonraki Build onu
    /// "güncel" sayıp atlar ve grup yarım kalmış hâlde temiz görünürdü (çıktı aracın kendisinin olduğundan defter
    /// kipinde okunur ve orada çıktının tarihi eşleşen imzayı bozmaz — ARCHITECTURE §7.6; bunu yakalayacak başka
    /// mekanizma yoktur). <c>false</c> ⇒ persist YOK ve BAŞARILI üye dahil invalidate edilir.</param>
    /// <param name="cycleUnsettled">[cycle rounds] Tavana dayanmış bir SCC'nin başarılı üyesi ⇒ çıktı bir kuşak
    /// geride olabilir (bkz. <see cref="ProjectSucceededEvent.CycleUnsettled"/>).</param>
    /// <param name="failLogTail">Başarısızlık satırının <c>decision.log</c>'daki SONU; <c>null</c> ⇒ varsayılan
    /// <c>" (Nms)"</c>. İptal yolu <c>" (cancelled)"</c>, invoke-error yolu ise <c>""</c> verir (reason zaten
    /// hatanın kendisidir) — bu iki metin ortak gövdeye taşınırken DEĞİŞMEDEN korunur.</param>
    /// <param name="cycle">[RESOLVE 3.4] Yakınsayan grupta derlenen üyenin döngü kanıtı; tekil proje ve diğer her yol
    /// null geçer (persist döngü alanlarını null yazar).</param>
    private void ReportProjectResult(RunContext run, string projectId, BuildResult result, long durationMs,
        string? failReason, DepIssueResult depIssues, bool trustedResult, bool cycleUnsettled, string? failLogTail,
        CycleMemberRecord? cycle = null, IReadOnlyList<CycleReadSurface>? dependencySurfaces = null)
    {
        string name = NameOf(run, projectId);
        // [spec 2026-09-18 §6.1 · karar 10 · P4] Branch kesmesinden SONRA biten hiçbir sonucun arkasında durulmaz:
        // derlenen kaynak artık diskteki kaynak değildir. Başarı defterde kanıtsız hata olur (Trusted=false, gri
        // never built), hata kanıt sayılmaz — çökme kurtarmasıyla aynı defter hâli (§5.5). TEK kapı burasıdır;
        // SCC üyeleri de (ReportCycleMember) buradan geçer; derlenmeyen taşınan üyenin defter yenilemesi
        // (ReportCarriedCycleMember) aynı bayrağa uyar.
        lock (_gate) trustedResult &= !_interrupted;
        // [R3 final · O1] "depIssue var mı" koşulu TEK yerde türer (DepIssueRootsOf): kökler olay listesinin de defter
        // yazımının da kaynağıdır — ikisi ayrı hesaplanıp ayrışamaz.
        var depIssueRoots = DepIssueRootsOf(depIssues);
        IReadOnlyList<string>? depIssuesForEvent = depIssueRoots is null ? null : depIssues.All;
        // [R-M4b · spec 2026-09-18 §1-14] Defterin kararı TEK KEZ verilir ve İKİ tüketiciye gider: App'e giden
        // olay (ProjectFailedEvent.Evidence · ProjectSucceededEvent.Trusted) ve defter yazımı
        // (InvalidateBuildStateOnFailure). İkisi ayrı hesaplansaydı satır ile bir sonraki Sync ayrışabilirdi —
        // App metni yeniden sınıflandırmaz, başarının güvenilir olup olmadığını da tahmin etmez.
        bool invalidates = result != BuildResult.Succeeded || !trustedResult;
        string? evidenceSignature = null;
        try
        {
            // [final review O4] Kanıt kapısı da Complete garantisinin İÇİNDEDİR: fırlasa bile scheduler askıda
            // kalmaz (finally yine TAM BİR KEZ Complete eder).
            if (invalidates) evidenceSignature = FailureEvidenceSignature(run, projectId, failReason, trustedResult);
            if (result == BuildResult.Succeeded)
            {
                // [DEĞİŞEN KURAL — A2] depIssue TAŞIYAN bir success bağımlılığının BAYAT çıktısına link'lidir ve
                // yine derlenmelidir. Eskiden bu, "deftere HİÇ yazma" ile sağlanıyordu; ölçüldü ki o kural
                // defterin ilerlemesini tamamen durduruyor (bir koşuda 24 hata depIssue'yu 96 projeye yaydı ve
                // 74 başarının SIFIRI yazıldı → incremental fiilen devre dışı, her Sync "hepsi derlenecek").
                // Kayıt artık NOTLA ve KÖKLERİYLE yazılır; yeniden derleme kararını WillBuildEvaluator o nottan
                // verir (WaitingForDependency) ve koşu kök düzeldiğinde uygular (ConditionalRebuild).
                // Kazanç: sha çifti ve kart artık gerçeği gösterir.
                // [A2 fix-4] Ayrımı ikinci kez TÜRETME: "depIssue var mı ⇒ kökler" kuralı DepIssueRootsOf'ta TEK yerdedir ve
                // sonucu (depIssueRoots) hem olay listesini (depIssuesForEvent) hem bu persist'in köklerini besler — koşul bir
                // kez türer, ikisi yapısal olarak kilit adımdır (taşınan SCC üyesinin defter yenilemesi de oradan okur).
                // [cycle rounds · D3] trustedResult AYRI bir kapıdır ve KORUNUR: yakınsamayan grubun bayat (oturmamış)
                // üyesinin sonucu bir "başarı" değildir, imzası da anlamlı değildir — o hiç persist edilmez.
                // [tek proje · Clean] Temizlenen projenin kaydı SİLİNİR (persist edilmez): çıktı artık yok,
                // defter de onu bilmemeli — gerekçe BuildStateStore.Remove'da.
                if (run.MsBuildTarget == MsBuildTarget.Clean) ForgetBuildStateOnClean(run, projectId);
                else if (trustedResult)
                    PersistBuildStateOnSuccess(run, projectId, durationMs,
                        depIssueRoots: depIssueRoots, cycle: cycle, dependencySurfaces: dependencySurfaces);
                // [final review I1] Trusted = defter bu başarıyı başarı olarak tuttu mu (invalidates'in tersi);
                // tutmadıysa App satırı, bir sonraki Sync'in okuyacağı "kanıtsız hata" hâliyle çizer.
                run.Events.TryWrite(new ProjectSucceededEvent(run.RunId, projectId, durationMs, depIssuesForEvent,
                    cycleUnsettled, Trusted: !invalidates));
                Decide(run.Logs, string.Format(CultureInfo.InvariantCulture,
                    "{0}: succeeded ({1}ms)", name, durationMs));
            }
            else
            {
                string reason = failReason!;
                run.Events.TryWrite(new ProjectFailedEvent(run.RunId, projectId, durationMs, reason, depIssuesForEvent,
                    Evidence: evidenceSignature is not null));
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
            // [cycle rounds] Arkasında durulamayan bir BAŞARI da (yakınsamayan SCC'nin bayat yeşil üyesi) buradan geçer.
            // [spec 2026-09-18 §1-14] reason ve trustedResult birlikte TAŞINIR: invalidate artık nedene göre
            // yazar (kanıtlı derleyici hatası ⇔ imza+zaman; kanıtsız ⇔ yalnız LastResult/LastRunAt).
            if (invalidates) InvalidateBuildStateOnFailure(run, projectId, evidenceSignature);
            // [spec 2026-09-18 §5.5] Sonuç raporlandı VE defter yazıldı — proje artık uçuşta değil. En SONDA: motor
            // bu iki yazım arasında ölürse proje hâlâ listededir ve açılış onu geçersizler. Complete'ten sonra
            // olduğu için defter I/O'su scheduler'ı asla askıda bırakamaz (TrackInFlight zaten fırlatmaz).
            TrackInFlight(ledger => ledger.Remove(projectId));
        }
    }

    /// <summary>Projenin görünen adı; id her zaman plan'dadır (scheduler aynı plan'dan sürülür) ama arama
    /// FIRLATMAYAN biçimde yapılır: buradan gelecek bir exception Complete'i atlatabilirdi.</summary>
    private static string NameOf(RunContext run, string projectId) =>
        run.NodeById.TryGetValue(projectId, out var node) ? node.Name : projectId;

    /// <summary>[Fix round 1 — I1] decision.log'da bir SCC'yi ANAN ad: build-order'daki ilk üyenin adı. Başlık, tur,
    /// kayıp, karar ve retry satırları grubu bu TEK kuraldan anar (CycleRoundStartedEvent'in lideri de aynı üyedir).
    /// İmza temsilcisi (<see cref="CycleGroups.SignatureRepresentative"/>) ayrı bir sorudur: o "imza hangi üyeden
    /// okunur", bu "kullanıcı grubu hangi adla görür".</summary>
    private static string CycleGroupName(IReadOnlyList<string> buildOrderMembers, Func<string, string> nameOf) =>
        nameOf(buildOrderMembers[0]);

    /// <summary>
    /// [grup koşullu atlama] Dispatch edilmiş bir SCC'yi, uygunsa DERLEMEDEN atlar ve <c>true</c> döner.
    /// Uygunluk saf kuralda (<see cref="ConditionalRebuild.GroupAppliesTo"/>); kökler üye başına
    /// <see cref="ConditionalRebuild.Decide"/>'a sorulur ve TEK düzelen kök grubu normal derletir (o zaman
    /// <c>false</c> döner ve çağıran <see cref="BuildCycleGroupAsync"/>'i koşar). Karar anı grubun dispatch
    /// anıdır: dışarıdaki tüm bağımlılıklar (dolayısıyla kökler) bu koşuda terminaldir — tekil projenin
    /// <see cref="TrySkipWhileDependencyStillFails"/> kuralıyla aynı an, aynı kanıt sırası.
    ///
    /// <para>Atlamada tekil yolun sözleşmesi birebir korunur: defter kaydına DOKUNULMAZ (not, kökler, imza
    /// aynen kalır — kök düzelince derlenecek), kökler birikime yazılır (bağımlılar notu miras alır),
    /// her üye kendi <c>finally</c>'siyle TAM BİR KEZ <see cref="ReadySetScheduler.Complete"/> edilir.
    /// Karar hesabında beklenmedik hata ⇒ grup DERLENİR (güvenli yön, tekil yol gibi).</para>
    /// </summary>
    private bool TrySkipGroupWhileDependenciesStillFail(RunContext run, IReadOnlyList<string> allMembers)
    {
        // Yalnız gerçekten dispatch edilmiş üyeler Complete borcu taşır (BuildCycleGroupAsync ile AYNI filtre).
        var completedAtDispatch = run.Scheduler.Completed;
        var members = allMembers
            .Where(id => run.NodeById.ContainsKey(id) && !completedAtDispatch.ContainsKey(id))
            .ToList();
        if (members.Count == 0) return false;

        try
        {
            var nodes = new List<ProjectNode>(members.Count);
            foreach (string id in members)
            {
                if (!run.NodeById.TryGetValue(id, out var node)) return false; // savunmacı: bilinmeyen üye → derle
                nodes.Add(node);
            }
            if (!ConditionalRebuild.GroupAppliesTo(nodes, run.Mode)) return false;

            foreach (var node in nodes)
            {
                if (node.WillBuild != true) continue; // güncel üye kısıt üretmez
                var recorded = run.LedgerAtStart?.GetValueOrDefault(node.Id);
                if (ConditionalRebuild.Decide(recorded?.DepIssueRoots, run.Scheduler.Completed,
                        run.NodeById.ContainsKey, run.LedgerAtStart, ReasonOf(run))
                    != ConditionalRebuildVerdict.DependencyStillFailing)
                    return false; // bir kök düzeldi: grup normal derlenir
            }
        }
        catch (Exception ex)
        {
            console("warning: cycle group conditional check failed ("
                + NameOf(run, allMembers[0]) + ") — building: " + ex.Message);
            return false;
        }

        // Karar kesin: grup atlanır. Raporlama tekil yolun karşılığıdır; güncel üye kendi gerekçesini taşır.
        foreach (string id in members)
        {
            var node = run.NodeById[id];
            try
            {
                if (node.WillBuild != true)
                {
                    ReportSkipped(run.Events, run.Logs, run.RunId, id, node.Name,
                        SkipReasons.UpToDate, cycleUnconverged: false);
                    continue;
                }
                // GroupAppliesTo + Decide==StillFailing bu üyenin kayıtlı kökleri olduğunu garantiler.
                var roots = run.LedgerAtStart?.GetValueOrDefault(id)?.DepIssueRoots;
                if (roots is { Count: > 0 }) run.DepIssuesById[id] = roots; // birikim: miras kaybolmaz
                string described = string.Join(", ", ConditionalRebuild.DescribeStillFailingRoots(
                    roots, run.Scheduler.Completed, run.NodeById.ContainsKey, run.LedgerAtStart, ReasonOf(run),
                    rootId => run.NodeById.GetValueOrDefault(rootId)?.Name ?? Path.GetFileNameWithoutExtension(rootId)));
                ReportSkipped(run.Events, run.Logs, run.RunId, id, node.Name,
                    SkipReasons.DependencyStillFailing, cycleUnconverged: false, detail: described);
            }
            finally { run.Scheduler.Complete(id, BuildResult.Skipped); }
        }
        return true;
    }

    /// <summary>
    /// [cycle rounds] Bir SCC'nin tüm yaşam döngüsü. Üyeler her turda <see cref="CycleRoundLevels"/>'ın
    /// BARİYERLİ seviyeleriyle invoke edilir: komşu olmayan üyeler aynı seviyede eşzamanlı, HERHANGİ yönde
    /// doğrudan kenar komşuları (ve birbirinin paylaşılan kopyasına dokunabilenler) asla — A, B.dll'i okurken B
    /// aynı dosyayı yazıyor olurdu; bariyerler örtüşmediği için bu yapısal olarak imkânsızdır. Eşzamanlılık
    /// koşunun paralellik tavanını aşamaz (üye, invoke'u boyunca koşunun ortak semaforundan bir slot tutar).
    ///
    /// <para>ARA TUR SONUÇLARI YAYILMAZ. SCC tek bir derleme birimidir (§7.3, tek bileşik imza); yarı bitmiş bir
    /// birimi "bitti" saymak progress'i geri götürür ve ETA'yı yanıltır. Yalnız son turun sonucu raporlanır,
    /// süre ise turların TOPLAMIDIR (gerçek maliyet). Her turun iki ucu ise ilan edilir, sonuç taşımadan:
    /// <see cref="ProjectStartedEvent"/> üye slotunu aldığında (o an gerçekten derleniyordur),
    /// <see cref="CycleMemberHeldEvent"/> derlemesi bitip slotu bırakmadan önce (artık grubunu bekliyordur).</para>
    ///
    /// <para><see cref="ReadySetScheduler.Complete"/> dispatch edilmiş her üye için TAM BİR KEZ, <c>finally</c>
    /// içinden çağrılır (stop/iptal/beklenmeyen hata dahil) — biri atlanırsa <see cref="ReadySetScheduler.IsDone"/>
    /// asla true olmaz ve run askıda kalır.</para>
    ///
    /// <para><b>Stop'ta grup ile tekil proje SİMETRİK DEĞİLDİR</b> ve bu kasıtlıdır: graceful stop, in-flight
    /// TEK bir projenin bitip persist etmesine izin verir, ama bir SCC'yi İÇİNDE BULUNDUĞU TURUN sonunda
    /// keser — kesilen grup yakınsama ölçütüne (kanıtla bayatsız tur ya da iki ardışık yeşil tur) varmadığı için
    /// asla yakınsamış sayılmaz (hiçbir şey
    /// persist edilmez, tüm üyeler invalidate olur). Gerekçe "SCC tek bir derleme birimidir": burada "in-flight
    /// iş" tek bir invoke değil, TURLARIN TAMAMIDIR; kalan turları stop'a rağmen sürdürmek Stop'u anlamsız
    /// kılardı (32 üyeli bir grupta 64 invoke daha).</para>
    /// </summary>
    private async Task BuildCycleGroupAsync(RunContext run, IReadOnlyList<string> allMembers, CancellationToken ct)
    {
        // [TryDispatch sözleşmesi] Dispatch ANINDA zaten Completed'ta olan (ör. tohumla Skipped girilmiş —
        // Build ve Cycles tohumu bir SCC'yi hep TÜM üyeleriyle birden tohumlar, hiçbir zaman kısmi değil; yani bu
        // savunmacıdır) ya da plan'da karşılığı olmayan üye in-flight'a HİÇ girmedi; onun için Complete
        // çağırmak fırlatırdı. Tur döngüsü bu yüzden yalnız GERÇEKTEN dispatch edilmiş üyeler üzerinde çalışır —
        // ama grup-içi kenar hesabı TÜM üyelere bakar (dairesel kenar, üye terminal olsa da dairesel kalır).
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

        // [API kısa devresi] Grup-içi kenarların YÜZEY takibi. Kaynak turlar arasında değişmez; bir üyenin
        // sonucunu yalnız OKUDUĞU grup-içi çıktı yüzeyinin değişmesi değiştirebilir. Kimin kimi okuduğu plan
        // kenarlarından gelir (kenar, üye terminal olsa da kenardır — allMembers); üreticinin dosyaları ise kanıt
        // yolu + beslenen kopyalarıdır (IncrementalPlan.OutputsById) ve yüzey DOSYA BAŞINA tutulur: hangisinin
        // okunduğunu üyenin derleyici satırı söyler (CycleReadFiles) — bilinmiyorsa hepsi izlenir.
        var memberSet = new HashSet<string>(allMembers, StringComparer.OrdinalIgnoreCase);
        var siblingDeps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        // [D7-b] Grup DIŞI doğrudan bağımlılıklar: üye terimi onları taşımaz; tur 1 kararı (kural i-b) kayıttaki bağımlılık
        // yüzeyini grup başında okunan diskle karşılaştırır.
        var outsideDeps = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in members)
        {
            var dependencies = run.NodeById.TryGetValue(id, out var node) ? node.Dependencies : [];
            siblingDeps[id] = [.. dependencies.Where(memberSet.Contains)];
            outsideDeps[id] = [.. dependencies.Where(dep => !memberSet.Contains(dep))];
        }

        // [paylaşılan kopya çakışması] Kenar olmadan da aynı dalgada derlenemeyecek üyeler: adları üzerinden
        // birbirinin (ya da okuduğu projelerin) paylaşılan kopyasını yazabilenler. Adlar ve TÜM bağımlılıklar
        // plandan gelir; kural Core'da.
        var mayCollide = CycleRoundLevels.SharedCopyCollisions(
            id => NameOf(run, id),
            id => run.NodeById.TryGetValue(id, out var node) ? node.Dependencies : []);

        var outputsById = run.Incremental?.OutputsById;
        // [PERF Faz E1] decision.log'da grubu ANAN ad (CycleGroupName: build-order'daki ilk üye) — başlık, tur, kayıp,
        // karar ve retry satırları aynı grubu tek adla anar. Satır metinlerinin sahibi CycleDecisionLines'tır.
        string group = CycleGroupName(members, id => NameOf(run, id));
        // Üreticinin bilinen dosyaları → dosya başına yüzey özeti; tek bir okunamayan dosya kanıt değildir (null).
        // [PERF Faz E1] null dönüşte HANGİ dosyanın NEDEN kanıt olamadığı da döner: kanıt kaybı decision.log'a
        // adıyla yazılır (yol türetilemedi ⇒ dosya yok; yüzey özeti okunamadı ⇒ kilitli ya da bozuk).
        IReadOnlyDictionary<string, string>? SurfaceStateOf(string producerId, out string lostFile, out string lostReason)
        {
            lostFile = CycleDecisionLines.NoFile;
            lostReason = CycleDecisionLines.NoEvidencePathReason;
            if (outputsById is null || !outputsById.TryGetValue(producerId, out var outputs)) return null;
            var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { outputs.Evidence };
            foreach (string fed in outputs.FedCandidates) files.Add(fed);
            var state = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                if (_apiSurface(file) is not { } hash) // okunamayan dosya kanıt değildir
                {
                    lostFile = file;
                    lostReason = CycleDecisionLines.UnreadableReason;
                    return null;
                }
                state[file] = hash;
            }
            return state;
        }

        var producers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var deps in siblingDeps.Values)
            foreach (string dep in deps)
                producers.Add(dep);

        // hashMode: kanıt YARIM olmaz — tek bir üreticinin bile yüzeyi koşu başında okunamıyorsa (kanıt yolu
        // türetilememiş ya da dosya bozuk/kilitli) grup bugünkü tam-tur davranışında kalır: kısmi bilgiyle
        // verilecek erken bir karar kanıt değil tahmin olurdu. "Dosya yok" ise okunabilir bir DURUMDUR
        // (ApiSurfaceHash.Absent) — hiç derlenmemiş bir grup da kısa devreden yararlanır.
        // Üretici → dosya başına yüzey. Tazelenen üreticinin haritası BÜTÜNÜYLE değiştirilir (yerinde
        // güncellenmez): bir üyenin okuma anı kaydı haritayı referansla tutar ve sonradan kaymaz.
        var surfaceState = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        bool hashMode = outputsById is not null && producers.Count > 0;

        // [DEĞİŞEN KURAL] Yarıda kesilen grupta HİÇBİR üyenin sonucunun arkasında durulamaz: tur 1'de yeşile
        // dönmüş bir üye de Failed raporlanır. Eskiden yalnız reason yazılır, sonuç KORUNURDU — o üye ÖNCEKİ
        // (ara) turunun sonucuyla ProjectSucceededEvent alırdı; tam olarak "ara tur sonucu nihai sonuç diye
        // yayılmaz" kuralının ihlali, üstelik tekil proje yolu iptalde daima Failed("stopped") raporlar.
        // Persist tarafında zaten yürürlükte olan ilkenin (kesilen grup hiçbir şey persist etmez)
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
        string interruptedReason = FailureReasons.Stopped;
        // [R3c3] İlk dispatch yapıldı mı: öncesinde fırlayan istisna grup BAŞI hatasıdır (yüzey hash'i, tur 1 seçimi, başlık
        // satırları), sonrasındaki "invoke error"dır — iki neden metni de InvokeErrorReason ile AYNI yerde sahiplenilir.
        bool groupDispatched = false;
        // [Task 3] finally'deki RecordCycleOutcome çağrısına geçecek — try içinde tanımlanırsa scope dışına
        // taşmazlardı, decision'la AYNI kapsamda dururlar.
        int roundsRun = 0;
        int lastFailedCount = 0;
        // [suçlu kırmızı] NoProgress'i getiren turda hem BAŞARISIZ hem de OTURMUŞ (staleNow dışı) üyeler:
        // okudukları her grup-içi yüzey nihaiyken derleyici hatası verdiler — girdileri bir sonraki denemede
        // de birebir aynı olacağından hataları sıradan bir Build hatasıyla aynı kalitede KANITTIR ve satır
        // kırmızısını hak eder. Yalnız yüzey kanıtı varken dolar; legacy NoProgress'te (suçlu ayırt edilemez)
        // null kalır ve bugünkü kanıtsız davranış sürer.
        HashSet<string>? provenHopeless = null;
        // [D3] Son turun bayat kümesi: yakınsamayan grupta hangi başarının arkasında durulabileceğini söyler. Yüzey
        // kanıtı yokken null kalır ve hiçbir yeşil güvenilmez (bugünkü kural).
        HashSet<string>? staleAtEnd = null;
        try
        {
            // [R3c2 · kesilme garantisi] Grup başı bloğu (yüzey hash'i, başlık ve kanıt kaybı satırları) bu
            // try'ın İÇİNDEDİR: orada fırlayan beklenmeyen bir istisna da catch → FailEveryMember yolundan her
            // üyeyi raporlatır (eskiden try'ın dışındaydı ve üyeler hiç raporlanmazdı — koşu asılırdı).
            // [PERF Faz E1] Kanıt SESSİZCE kaybolmaz: ilk okunamayan üreticinin satırı, grup başlığından hemen sonra
            // yazılır. Çıktı haritası hiç yoksa (artımlı plan yok) kaybedilecek kanıt da yoktur — başlık "off" der.
            string? evidenceLoss = null;
            var groupHashClock = Stopwatch.StartNew();
            if (hashMode)
            {
                // [PERF Faz E2] Üreticiler PARALEL okunur (derece: tek IO paralelliği sabiti). Kanıt İLK hatada kapanır:
                // kaybı yalnız CAS'ı kazanan çağrı yazar ve döngü yeni üretici başlatmaz; sözlük yazımı kilit altında.
                int lost = 0;
                Parallel.ForEach(producers, new ParallelOptions { MaxDegreeOfParallelism = IoParallelism.Degree },
                    (producerId, loop) =>
                    {
                        if (Volatile.Read(ref lost) != 0) return;
                        if (SurfaceStateOf(producerId, out string lostFile, out string lostReason) is { } initial)
                        {
                            lock (surfaceState) surfaceState[producerId] = initial;
                        }
                        else if (Interlocked.CompareExchange(ref lost, 1, 0) == 0)
                        {
                            evidenceLoss = CycleDecisionLines.EvidenceUnavailable(group, NameOf(run, producerId), lostFile, lostReason);
                            loop.Stop();
                        }
                    });
                hashMode = lost == 0;
                // [D7-b] Grup DIŞI üreticiler: yalnız kanıt dosyası okunur (SurfaceOf — koşu önbelleği; DependencySurfaces ile
                // AYNI dosya, beslenen kopyalar DEĞİL). Okunamayan ya da kanıt yolu türetilemeyen üretici kanıtı düşürmez ve
                // surfaceState'e girmez ⇒ onu okuyan üye kural (i-b) ile gerekli (güvenli yön). Başlıktaki üretici sayısı grup
                // içidir.
                if (hashMode)
                {
                    string[] outsideProducers = [.. outsideDeps.Values.SelectMany(deps => deps).Distinct(StringComparer.OrdinalIgnoreCase)];
                    Parallel.ForEach(outsideProducers, new ParallelOptions { MaxDegreeOfParallelism = IoParallelism.Degree }, dep =>
                    {
                        if (SurfaceOf(run, dep) is { } surface && SurfaceGate.Persistable(surface.Hash) is { } hash)
                            lock (surfaceState)
                                surfaceState[dep] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [surface.File] = hash };
                    });
                }
            }
            Decide(run.Logs, CycleDecisionLines.GroupStarted(group, members.Count, producers.Count, hashMode,
                groupHashClock.ElapsedMilliseconds));
            if (evidenceLoss is not null) Decide(run.Logs, evidenceLoss);

            HashSet<string>? previousFailed = null;
            // [API kısa devresi] Turun derleyeceği üyeler: tur 1 HERKES; sonraki turlar yalnız bayat bağlanmış
            // (staleNow) üyeler — kanıt yoksa yine herkes. Liste build-order sırasını korur; tur olayı da
            // O TURDA derlenecek sayıyı taşır (alanın sözleşmesi).
            // [RESOLVE 3.4 · karar 2] Yüzey kanıtı ve üye terimleri varken tur 1 de yalnız GEREKEN üyeleri derler. Karar
            // Core'dadır (CycleMemberNeed.Decide — saf); burada yalnız uygulanır. Gerekmeyen üye TAŞINIR: sonucu güvenilir
            // kayıttan Succeeded, okuma durumu kayıttan — tur sonu bayatlık sorusuna aynen girer (karar 3, kural
            // değişmez), bayatlarsa sonraki turda derlenir. Fast koşusunda terim haritası BOŞTUR: GetValueOrDefault ⇒
            // null ⇒ "no member term", herkes gerekli (ayrı dal yok).
            IReadOnlyList<string> toBuild = members;
            bool roundOneCarried = false; // [R3c2] tur 1'de taşınan üye var mı — iki-yeşil kuralının tabanı (tur sonu)
            // [Build cycle derler] Rebuild "önbelleği yok say"dır: üye ihtiyacı sorulmaz, herkes tur 1'de derlenir.
            // hashMode DOKUNULMAZ — tur sonu bayatlık kararı yine kanıtla verilir (Fast'teki "terim yok" dalıyla aynı
            // sonuç, ama sebebi decision.log'a açıkça yazılır).
            if (run.Mode == RunMode.Rebuild)
                Decide(run.Logs, CycleDecisionLines.RebuildCompilesEveryMember(group));
            else if (hashMode && run.Incremental is { MemberTermById: { } memberTerms } incremental)
            {
                var need = CycleMemberNeed.Decide(members,
                    id => new CycleMemberNeed.MemberEvidence(run.LedgerAtStart?.GetValueOrDefault(id),
                        memberTerms.GetValueOrDefault(id), incremental.ChecksById?.GetValueOrDefault(id),
                        // grup içi bağımlılıklar: okuma durumunu besleyen AYNI kardeş haritası (ikinci hesap yok)
                        siblingDeps[id], OutsideDependencies: outsideDeps[id]),
                    surfaceState, run.EngineFingerprint);
                toBuild = need.ToBuild;
                roundOneCarried = need.CarriedReadStates.Count > 0;
                foreach (string id in need.ToBuild)
                    Decide(run.Logs, CycleDecisionLines.RoundOneNeed(NameOf(run, id), need.Reasons[id]));
                foreach (var (id, carriedReads) in need.CarriedReadStates)
                {
                    var carried = state[id];
                    carried.Result = BuildResult.Succeeded;
                    carried.Carried = true;
                    carried.ReadStates = carriedReads.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
                }
            }
            for (int round = 1; decision == CycleRoundDecision.Continue; round++)
            {
                run.Events.TryWrite(new CycleRoundStartedEvent(
                    run.RunId, members[0], round, CycleRoundPolicy.RoundCap, toBuild.Count));

                // [PERF Faz E1] Tur satırının ölçüleri: turun süresi, derleme sonrası hash süresi (üyelerin TOPLAMI —
                // aynı seviyedeki hash'ler eşzamanlı koşabilir) ve koşulan seviye sayısı.
                var roundClock = Stopwatch.StartNew();
                long roundHashTicks = 0;
                int levelsRun = 0;
                bool cutShort = false;
                // [seviyeli turlar] Üyeler CycleRoundLevels'ın BARİYERLİ seviyeleriyle derlenir: komşu olmayan
                // üyeler aynı seviyede EŞZAMANLI (koşunun paralellik tavanı InvokeOnceAsync'teki ortak
                // sema-forla korunur), HERHANGİ yönde doğrudan kenar komşuları ve paylaşılan kopyaya dokunanlar
                // (mayCollide) asla — biri diğerinin DLL'ini okurken öteki aynı dosyayı yazamaz; bariyerler
                // örtüşmediği için torn read yapısal olarak imkânsızdır. Bir seviye TAMAMEN bitmeden sonraki
                // başlamaz: önceki seviyedeki kardeş taze, sonrakindeki önceki nesliyle okunur — hangisinin bir
                // tur daha gerektirdiğine tur sonu kararı bakar.
                async Task CompileOneAsync(string id)
                {
                    // [§4.5] Stop istendiyse turun KALAN üyeleri de dispatch EDİLMEZ. Graceful stop'un
                    // sözleşmesi "yeni hiçbir şey dispatch edilmez, in-flight child'lar biter"dir ve her üye
                    // YENİ bir MSBuild.exe child'ıdır — kapı üye BAŞLAMADAN kontrol edilir; başlamış seviye
                    // arkadaşları Task.WhenAll ile DRAIN edilir. Turu yarıda kesmenin bedeli yoktur: yarıda
                    // kesilen grup zaten her üyesini Failed'a çevirir (aşağıdaki FailEveryMember).
                    if (StopRequested) { cutShort = true; return; }

                    var member = state[id];
                    Dictionary<string, IReadOnlyDictionary<string, string>>? read = null;
                    List<string>? compiled = null;
                    if (hashMode)
                    {
                        // Üyenin ŞU AN okuyacağı kardeş yüzeyleri — tur sonu bayatlık kararının referansı.
                        // Kilit: aynı seviyedeki bir üye KENDİ yüzeyini yazarken sözlük okunuyor olabilir
                        // (farklı anahtar, aynı gövde); üyenin kendi bağımlılıkları komşu ayrımı gereği bu
                        // seviyede DEĞİLDİR, değerleri seviye boyunca sabittir.
                        read = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                        lock (surfaceState)
                            foreach (string dep in siblingDeps[id])
                                read[dep] = surfaceState[dep];
                        member.ReadStates = read; // derleme bitince okunan kopyaya daraltılır (aşağıda)
                        compiled = [];
                    }

                    // [okunan dosya kanıtı] Derleyicinin komut satırı, üyenin kardeşleri hangi dosyalardan
                    // okuduğunu söyler. Satırlar pump thread'lerinden gelir; kilit, derleme sonundaki okumanın
                    // hepsini görmesini garanti eder.
                    void ObserveCompilerLine(string line)
                    {
                        if (CompilerReferences.Parse(line) is { } references)
                            lock (compiled!) compiled.AddRange(references);
                    }

                    // [dalga görünürlüğü] Üye MSBuild sırasını (slot) ALDIKTAN SONRA "derleniyor" ilan edilir,
                    // "bitti, grubunu bekliyor" ilanı (CycleMemberHeldEvent) ise slot BIRAKILMADAN önce yazılır.
                    // App satırı bu iki ilanla "derleniyor"a alıp çıkarır; ilan edilen derleme sayısı böylece
                    // hiçbir anda koşunun paralelliğini aşamaz. Eskiden ilan slot beklenmeden yapılıyordu: sıraya
                    // giren dalga üyeleri de "derleniyor" görünürdü (5 uydulu dalga, paralellik 2'de beşi birden).
                    await run.InvokeSlots.WaitAsync(ct);
                    bool announced = false;
                    InvokeOutcome? outcome = null; // slot bırakıldıktan SONRAKİ hash kararına taşınır
                    try
                    {
                        // [§4.5] Sıra beklenirken Stop düşmüş olabilir: üye henüz BAŞLAMADI, başlatılmaz. Yukarıdaki
                        // kapının aynısıdır — dalgalı turda "başlamak" slotun alındığı andır; kapı yalnız slottan
                        // önce dursaydı sırasını bekleyen üye Stop'tan SONRA yeni bir MSBuild.exe başlatırdı.
                        if (StopRequested) { cutShort = true; return; }
                        TrackInFlight(ledger => ledger.Add(id)); // [§5.5] her tur yeni bir dispatch; sonuç ReportProjectResult'ta düşer
                        run.Events.TryWrite(new ProjectStartedEvent(run.RunId, id, NameOf(run, id)));
                        announced = true;
                        groupDispatched = true; // [R3c3] bundan sonraki istisna "invoke error"dır; öncesi grup başı hatasıdır
                        // [R3c3] Proje logu ilk derlemede TEMBEL açılır: hiç derlenmeyen (taşınan) üye boş bir dosya bırakmaz,
                        // sıradan güncel atlama gibi dosya almaz. Açılınca grubun kalan turları boyunca AÇIK kalır —
                        // OpenProjectLog truncate eder (FileMode.Create): tur başına açmak önceki turların logunu silerdi ve
                        // satır numaraları her turda 1'e dönerdi. Açıklama satırı YAZILMAZ: projenin ilk satırı gerçek MSBuild
                        // komut satırıdır (v7Δ-7). Derlenen üye sayısı kadar dosya tanıtıcısı açık kalır — 32 üye için kabul edilir.
                        var memberLog = member.Log ??= run.Logs.OpenProjectLog(id);
                        // [restore-once] bu koşuda bir önceki turu BAŞARILI bitmiş üye restore prologunu yeniden ödemez
                        // (gerekçe InvokeOnceAsync'te); başarısız üye yeniden restore alır. [R3c2] Taşınan üyenin
                        // Succeeded'ı kayıttan gelir, bu koşunun derlemesi değildir: ilk derlemesi (hangi turda olursa)
                        // restore kararından geçer — Carried ilk invoke'ta düşer.
                        outcome = await InvokeOnceAsync(run, id, member.DepIssues, memberLog, ct,
                            suppressRestore: member.Result == BuildResult.Succeeded && !member.Carried,
                            observeLine: compiled is null ? null : ObserveCompilerLine);
                        member.DurationMs += outcome.DurationMs;         // süre TURLARIN TOPLAMI
                        member.Result = outcome.Result;
                        // [RESOLVE 3.4] Bayatlayıp derlenen taşınan üye artık taşınmıyor: raporu ve defteri bu derlemenin.
                        // Sonuçla birlikte try içinde yazılır — aşağıdaki E2 sırasının (1). adımı.
                        member.Carried = false;
                        if (read is not null)
                        {
                            List<string> references;
                            lock (compiled!) references = [.. compiled];
                            bool succeeded = outcome.Result == BuildResult.Succeeded;
                            var tracked = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
                            foreach (var (dep, files) in read)
                                tracked[dep] = CycleReadFiles.Tracked([.. files.Keys], references, succeeded)
                                    .ToDictionary(file => file, file => files[file], StringComparer.OrdinalIgnoreCase);
                            member.ReadStates = tracked;
                        }
                        if (outcome.Result != BuildResult.Succeeded)
                            member.FailReason = outcome.FailReason;
                    }
                    finally
                    {
                        // Ara tur sonucu yine YAYILMAZ: ilan sonuç taşımaz, yalnız "derleme bitti" der. İstisnayla
                        // biten invoke de bitmiştir — grup onu kesip her üyeyi Failed raporlayana dek satır bekler.
                        if (announced) run.Events.TryWrite(new CycleMemberHeldEvent(run.RunId, id));
                        run.InvokeSlots.Release();
                    }

                    // [PERF Faz E2] SIRA — buraya yazım ekleyen (RESOLVE 3.4) bunu korur: (1) sonuç üyeye try içinde
                    // yazılır; (2) CycleMemberHeldEvent slot bırakılmadan ÖNCE yazılır (finally); (3) derleme sonrası
                    // hash BURADA, slot dışında okunur — büyük bir üreticinin okunması koşunun MSBuild kapasitesinden
                    // düşmez, sıradaki üye bu arada derlemeye başlar; (4) surfaceState yazımı ve kanıtın kapanışı kilit
                    // altındadır (aynı seviyedeki üyeler sözlüğü okuyor olabilir); (5) CompileOneAsync hash yazılmadan
                    // dönmez ⇒ seviye bariyeri sonraki seviyeye taze yüzeyi verir. Stop'un return'ü ve istisna buraya gelmez.
                    if (outcome?.Result == BuildResult.Succeeded && hashMode && producers.Contains(id))
                    {
                        // Slot devri boş bir thread'e bağlı kalmasın: hash ayrı bir iş öğesine bırakılır ve sıradaki
                        // üyenin devamı (Release'in kuyruğa koyduğu) bu thread'de hemen koşabilir. Bırakılmasaydı devir,
                        // hash bu thread'i tutarken başka bir thread'in boşalmasını beklerdi — yüklü test havuzunda
                        // ölçüldü: sıradaki üye 200 ms tavanında başlayamadı (12 koşunun 3'ünde).
                        await Task.Yield();
                        // Taze çıktı yazıldı: yüzeyi ŞİMDİ oku — sonraki seviyeler ve tur sonu bunu görür.
                        // Dosya okuma kilit DIŞINDA, yazım kilit İÇİNDE (tek gövde, farklı anahtarlar).
                        long hashStart = Stopwatch.GetTimestamp();
                        var fresh = SurfaceStateOf(id, out string lostFile, out string lostReason);
                        Interlocked.Add(ref roundHashTicks, Stopwatch.GetTimestamp() - hashStart);
                        if (fresh is not null) lock (surfaceState) surfaceState[id] = fresh;
                        else
                        {
                            // Okunamayan yüzey kanıt değildir: kısa devre bu gruptan çekilir, tam tura dönülür.
                            // Kayıp, koşu başındakiyle AYNI satırdır; eşzamanlı iki kayıptan yalnız ilki yazılır.
                            bool first;
                            lock (surfaceState) { first = hashMode; hashMode = false; }
                            if (first)
                                Decide(run.Logs, CycleDecisionLines.EvidenceUnavailable(group, NameOf(run, id), lostFile, lostReason));
                        }
                    }
                }

                foreach (var level in CycleRoundLevels.Compute(toBuild, id => siblingDeps[id], mayCollide))
                {
                    if (StopRequested) { cutShort = true; break; }
                    levelsRun++;
                    if (level.Count == 1) await CompileOneAsync(level[0]);
                    else await Task.WhenAll(level.Select(CompileOneAsync)); // tümü biter, ilk hata SONRA fırlar
                    if (cutShort) break;
                }

                // YARIDA KESİLEN TUR KARARA SOKULMAZ. Decide'a devam etmek en tehlikeli köşedir: ikinci turda
                // ve o ana kadarki üyeler yeşilken Decide(2, {}, {}) → Converged verirdi — hiç derlenmemiş
                // üyeler olduğu hâlde grup "yakınsadı" sayılır, DURDURULMUŞ bir koşu taze imza persist eder ve
                // bir sonraki Build'e "bu SCC güncel" diye yalan söylerdi. `decision` Continue'da bırakılır ⇒
                // aşağıdaki FailEveryMember her üyeyi invalidate eder, RecordCycleOutcome hiçbir şey yazmaz.
                if (cutShort) break;

                // failedNow üyelerin GÜNCEL (taşınan) sonucundan okunur: seçici turda derlenmeyen üyenin sonucu
                // son turundan taşınır — tam-tur kipinde bu, "bu turda patlayanlar"la birebir aynı kümedir.
                var failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string id in members)
                    if (state[id].Result != BuildResult.Succeeded)
                        failed.Add(id);

                // staleNow: son invoke'unda okuduğu bir kardeş yüzeyi ŞU AN farklı olan üyeler — yani bir tur
                // daha derlemenin sonucunu DEĞİŞTİREBİLECEĞİ üyeler. Karşılaştırılan, üyenin izlediği dosyalardır
                // (CycleReadFiles). Karar saf policy'de (CycleRoundPolicy).
                HashSet<string>? staleNow = null;
                // [PERF Faz E1] Bayatlığın KANITI: bayat bir üyenin okuduğu hâlinden farklılaşmış kardeş dosyaları
                // (tur satırının moved alanı). Bayat küme bununla değişmez — yalnız gerekçesi toplanır.
                SortedSet<string>? movedFiles = null;
                if (hashMode)
                {
                    staleNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    movedFiles = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (string id in members)
                    {
                        var read = state[id].ReadStates;
                        if (read is null) { staleNow.Add(id); continue; } // savunmacı: kaydı olmayan bayat sayılır
                        foreach (string dep in siblingDeps[id])
                        {
                            if (!read.TryGetValue(dep, out var seen)) { staleNow.Add(id); continue; }
                            foreach (string file in CycleReadFiles.MovedFiles(seen, surfaceState[dep]))
                            {
                                staleNow.Add(id);
                                movedFiles.Add(file);
                            }
                        }
                    }
                }

                roundsRun = round;
                lastFailedCount = failed.Count;
                decision = CycleRoundPolicy.Decide(round, failed, previousFailed, staleNow);
                staleAtEnd = staleNow;
                // [R3c2 · karar 3] Taşınan üyeli tur 1 "iki ardışık yeşil tur" kuralına taban olmaz: taşınan üyenin
                // Succeeded'ı bu koşunun derlemesi değil, kayıttan gelir. Kanıt varken policy zaten yalnız kanıtla
                // yakınsar (iki-yeşil kuralı staleNow null iken çalışır); bu koruma kanıtın düştüğü turlar içindir:
                // tur 1'den sonra yakınsama ya kanıtla (staleNow boş) ya da sonraki iki gerçek turla gelir.
                previousFailed = round == 1 && roundOneCarried ? null : failed;
                Decide(run.Logs, CycleDecisionLines.RoundEnded(group, round, decision,
                    staleNow is null ? null : members.Where(staleNow.Contains).Select(id => NameOf(run, id)).ToList(),
                    movedFiles?.ToList(), levelsRun, roundClock.ElapsedMilliseconds,
                    roundHashTicks * 1000 / Stopwatch.Frequency));
                if (decision == CycleRoundDecision.NoProgress && staleNow is not null)
                    provenHopeless = [.. failed.Where(id => !staleNow.Contains(id))];
                if (decision == CycleRoundDecision.Continue)
                    toBuild = staleNow is null ? members : [.. members.Where(staleNow.Contains)];
                // Stop istendiyse YENİ tur da AÇILMAZ. Turun TAMAMLANDIĞI hâlde stop'un tam tur sınırında
                // düştüğü dar durumun kapısıdır bu; Converged/NoProgress/CapReached ile biten tur buradan
                // geçmez, çünkü onlar GERÇEK kararlardır.
                if (decision == CycleRoundDecision.Continue && StopRequested) break;
            }
        }
        catch (OperationCanceledException)
        {
            // gerekçe zaten "stopped" — decision Continue'da kaldığı için aşağıdaki kapı uygular
        }
        catch (Exception ex)
        {
            interruptedReason = groupDispatched ? InvokeErrorReason(ex) : GroupStartErrorReason(ex);
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
                // [D3] Oturmuş üye: grup gerçek bir hükme vardı ve üyenin okuduğu hiçbir kardeş yüzeyi son tur sonunda
                // bayat değildi — nihai API'lere bağlandı, sonucu güvenilir. Converged'de herkes oturmuştur.
                bool settled = decision == CycleRoundDecision.Converged
                    || (decision != CycleRoundDecision.Continue && staleAtEnd is not null && !staleAtEnd.Contains(id));
                try
                {
                    // [RESOLVE 3.4 · D3] Hiç derlenmemiş ve oturmuş taşınan üye "up to date (carried)" raporlanır, defteri
                    // yenilenir. Kesilen grupta ya da bayatken taşınan üye ReportCycleMember'dan geçer: sonucu ya
                    // FailEveryMember'la Failed'dır ya da güvenilmez başarıdır — geçersizlenir.
                    if (member.Carried && settled)
                        ReportCarriedCycleMember(run, id, member.DepIssues);
                    else
                        ReportCycleMember(run, id, member.Result, member.DurationMs, member.FailReason, member.DepIssues,
                            successIsTrusted: settled,
                            failureIsEvidence: provenHopeless?.Contains(id) == true,
                            // Tavana dayanmış grubun BAYAT yeşil üyesi: çıktı bir kuşak geride olabilir.
                            cycleUnsettled: decision == CycleRoundDecision.CapReached
                                && member.Result == BuildResult.Succeeded && !settled,
                            // Döngü kanıtı güvenilir her başarıya yazılır (oturmuş üye dahil); hashMode düşmüşse yüzeyler null.
                            cycle: settled ? CycleRecordOf(run, id, hashMode ? member.ReadStates : null) : null,
                            // [D7-b] Güvenilir başarı grup DIŞI bağımlılıklarının yüzeyini de yazar (grup içi CycleReadSurfaces'ta):
                            // bir sonraki tur 1 kararının (i-b) tabanı. Taşınan üyenin yenilemesi bunlara dokunmaz.
                            dependencySurfaces: settled && member.Result == BuildResult.Succeeded
                                ? DependencySurfacesOf(run, id, excludedDeps: allMembers)
                                : null);
                }
                catch (Exception ex) { reportFailure ??= ex; }
            }
            // [Task 7 · D3] Üye raporlamasından SONRA: yukarıdaki döngü oturmamış üyeleri invalidate etmiştir; oturmuş
            // üyenin taze kaydı hafızayı da taşır (yalnız raporlar). Bu yalnız ÜZERİNE, hangi bileşik imzada
            // pes edildiğini ayrıca kaydeder. reportFailure varsa bile denenir — bir üyenin raporlama hatası
            // hafıza yazımını ENGELLEMEMELİDİR (aksi halde bir sonraki Cycles koşusu grubu yanlış raporlardı:
            // yazılmamış bir NoProgress'i tanıyamaz, silinmemiş bayat bir hafızayı ise tanırdı).
            long totalDurationMs = members.Sum(id => state[id].DurationMs); // [Task 3] üye sürelerinin TOPLAMI
            RecordCycleOutcome(run, allMembers, members, decision, roundsRun, lastFailedCount, totalDurationMs,
                compiledCount: members.Count(id => !state[id].Carried)); // [RESOLVE 3.4] taşınanlar derlenmedi
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
    ///
    /// <para>[Task 3] decision.log satırıyla AYNI kapıdan (yarıda kesilen grupta hiçbir şey yazılmaz/yayılmaz)
    /// bir de <see cref="CycleCompletedEvent"/> yayınlar — decision.log'daki karar bugüne dek yalnız disk
    /// dosyasında kalıyordu, App'in görebileceği bir kanalı yoktu.</para>
    /// </summary>
    /// <param name="roundsRun">Koşulan tur sayısı — <see cref="BuildCycleGroupAsync"/>'in döngü sayacı.</param>
    /// <param name="lastFailedCount">SON turun başarısız üye sayısı.</param>
    /// <param name="totalDurationMs">Üye sürelerinin TOPLAMI (turlar dahil) — <see cref="CycleMemberState.DurationMs"/>.</param>
    /// <param name="compiledCount">[RESOLVE 3.4] Bu koşuda DERLENEN üye sayısı — hiç derlenmemiş (taşınan) üyeler girmez.</param>
    private void RecordCycleOutcome(RunContext run, IReadOnlyList<string> allMembers,
                                    IReadOnlyList<string> members, CycleRoundDecision decision,
                                    int roundsRun, int lastFailedCount, long totalDurationMs, int compiledCount)
    {
        if (decision == CycleRoundDecision.Continue) return;
        // [spec 2026-09-18 §6.1 · T8 fix round 1 M4] Branch kesmesinden sonra verilen tur kararı da güvenilmez: üyeler
        // zaten güvenilmez raporlandı (ReportProjectResult), karar ne yayılır ne hafızaya yazılır — Stop'un
        // "Continue" kuralıyla aynı sonuç (Converged bir kesmede eski yakınsamama hafızasını silmemeli).
        lock (_gate) { if (_interrupted) return; }

        // Logda grubu ANAN ad, CycleRoundStartedEvent'in lideriyle AYNI olmalıdır (build-order'daki ilk üye) —
        // yoksa aynı grup iki kanalda iki farklı adla anılırdı. Bu, aşağıdaki İMZA temsilcisinden ayrı bir
        // sorudur: o "hangi üyenin imzası okunacak", bu "kullanıcı grubu hangi adla görüyor".
        string leader = CycleGroupName(members, id => NameOf(run, id));
        string? remembered = UpdateCycleNonConvergenceMemory(run, allMembers, members, decision);
        // [RESOLVE 3.4] Tek biçim: yakınsayan grubun satırı derlenen üye sayısını da söyler (CycleDecisionLines.Verdict),
        // diğer kararların satırı aynı.
        Decide(run.Logs, CycleDecisionLines.Verdict(leader, decision, members.Count, compiledCount, remembered));

        // ProjectId = members[0] (İD, ad DEĞİL) — CycleRoundStartedEvent'in lideriyle AYNI temsilci, satır
        // tıklanabilir kalsın diye.
        run.Events.TryWrite(new CycleCompletedEvent(run.RunId, members[0], MapOutcome(decision),
            members.Count, roundsRun, lastFailedCount, totalDurationMs, CompiledCount: compiledCount));
    }

    /// <summary>[Task 3] <see cref="CycleRoundDecision"/> → tel'e giden <see cref="CycleOutcome"/> — TEK switch.
    /// <see cref="CycleRoundDecision.Continue"/> buraya zaten gelmez (<see cref="RecordCycleOutcome"/> ilk
    /// satırda döner).</summary>
    private static CycleOutcome MapOutcome(CycleRoundDecision decision) => decision switch
    {
        CycleRoundDecision.Converged => CycleOutcome.Converged,
        CycleRoundDecision.NoProgress => CycleOutcome.NoProgress,
        CycleRoundDecision.CapReached => CycleOutcome.CapReached,
        _ => throw new ArgumentOutOfRangeException(nameof(decision), decision, "cycle decision cannot be Continue here"),
    };

    /// <summary>
    /// [Task 7] Yakınsamama hafızasının TEK yazıcısı — <see cref="BuildState.NonConvergentSignature"/>.
    ///
    /// <para><b>YALNIZ <see cref="CycleRoundDecision.NoProgress"/> ⇒ YAZ.</b> TÜM üyelerin alanına o anki
    /// bileşik imza yazılır; bir sonraki <c>Cycles</c> koşusu aynı imzayı görürse grup <see
    /// cref="BuildStateStore.IsCycleNonConvergent"/> ile TANINIR ve decision.log'a bir "retrying" satırı
    /// düşülür — [Task 7 · DEĞİŞEN KURAL] artık BLOKLAMAZ: grup yine dispatch edilir, yalnız RAPORLANIR.
    /// Kayıt hiç yoksa (SCC hiç derlenmemiş) burada taze bir <see cref="BuildState"/> açılır. Üyelerin raporu bu
    /// yazımdan ÖNCE koşar ve oradaki <see cref="InvalidateBuildStateOnFailure"/> kaydı bugün zaten açar; bu dal
    /// savunmacıdır — o yazım düşse de (warn-only) hafıza kaybolmaz.</para>
    ///
    /// <para><b><see cref="CycleRoundDecision.CapReached"/> ⇒ YAZMA.</b> İki karar aynı şey DEĞİLDİR ve ayrım
    /// KANITA dayanır. NoProgress "tur eklemek sonucu değiştiremez" demektir (bkz.
    /// <see cref="CycleRoundDecision.NoProgress"/>) — bir SIKIŞMA kanıtı. CapReached ise "hâlâ hareket var ama
    /// BÜTÇE bitti" demektir — kanıt değil, kesinti; tavan bilgi kaybettirmez, çünkü turlar diskteki duruma göre
    /// idempotenttir ve bir sonraki <c>Cycles</c> koşusu kaldığı yerden devam eder. Hafıza bugün yalnız
    /// RAPORLAR: bir sonraki <c>Cycles</c> koşusu grubu tanıyıp decision.log'a "did not converge at this
    /// signature" yazar — CapReached yazılsaydı o satır hiç kanıtlanmamış bir sıkışmayı raporlardı.
    /// <b>Tarihçe:</b> hafıza eskiden pre-skip de EDERDİ (bkz. Cycles tohumundaki
    /// <c>[Task 7 · DEĞİŞEN KURAL]</c> notu) — o dönemde hatırlanan bir CapReached, tam olarak yakınsamakta olan
    /// bir grubu (tur1 {A,B}, tur2 {A}, tur3 temiz) bir tur kala dondururdu; tek çıkış ilgisiz bir kaynak
    /// değişikliği olurdu. Kural o riskten doğdu.</para>
    ///
    /// <para><b>Converged (ve CapReached) ⇒ SİL.</b> [M3] Gerçek bir tur kararı eski hafızayı geçersiz kılar.
    /// Converged'de bu çoğunlukla bir yan etkiyle de olur: <see cref="PersistBuildStateOnSuccess"/> taze bir
    /// <see cref="BuildState"/> KURAR ve alan doğal olarak null'a döner ([A2]'den beri dep-issue taşıyan başarı
    /// da persist edilir). CapReached'te böyle bir yan etki YOKTUR: üyeler güvenilmez raporlanır ve
    /// <see cref="InvalidateBuildStateOnFailure"/> kaydı <c>with {…}</c> ile günceller — alan KORUNUR. Açık silme
    /// olmasa bir sonraki <c>Cycles</c> koşusu, grubun son kararı "hâlâ hareket vardı" iken, aynı imzada bayat bir
    /// NoProgress kanıtını raporlardı. Bu yüzden silme AÇIKÇA burada, hafızanın kendi yazıcısında yapılır (yan
    /// etkiye bırakılmaz).</para>
    ///
    /// <para><b>İmza temsilcisi</b> <see cref="CycleGroups.SignatureRepresentative"/>'dendir ve OKUYAN taraf
    /// (Cycles koşusunun kendi tanıma taraması) AYNI yardımcıyı çağırır — iki taraf kendi <c>[0]</c>'ını seçseydi listeler
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
        // temizleme dalına düşer — yani eski bir NoProgress hafızası da SİLİNİR. Bu kasıtlıdır: aynı imzada
        // tavana dayanan (yani hâlâ hareket eden) bir grubun bayat hafızası dursaydı, bir sonraki Cycles koşusu
        // onu "did not converge at this signature" diye raporlardı (bkz. doc'taki "SİL" paragrafı).
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

        /// <summary>Üyenin ilk derlemesinde TEMBEL açılan ve grubun kalan turları boyunca açık kalan proje logu; hiç
        /// derlenmediyse (taşınan üye) null — dosya da oluşmaz.</summary>
        public ProjectLogFile? Log { get; set; }

        /// <summary>[API kısa devresi] Üyenin SON invoke'u başlarken okuduğu kardeş yüzeyleri (üretici → izlenen
        /// dosya → yüzey özeti); hash-mode kapalıyken null. İzlenen dosyalar derleme bitince derleyicinin okuduğu
        /// kopyaya daraltılır (<see cref="CycleReadFiles"/>). Tur sonu bayatlık kararının referansıdır.</summary>
        public Dictionary<string, IReadOnlyDictionary<string, string>>? ReadStates { get; set; }

        /// <summary>[RESOLVE 3.4] Tur 1'de gerekmedi (karar 2): sonucu ve okuma durumu güvenilir kayıttan gelir, bu koşuda
        /// henüz derlenmedi. Bayatlayıp derlenince düşer (<c>CompileOneAsync</c>); grup gerçek bir hükme vardığında oturmuş
        /// ve hâlâ taşınıyorsa "up to date (carried)" raporlanır.</summary>
        public bool Carried { get; set; }
    }

    /// <summary>[RESOLVE 3.4] Yakınsayan grupta derlenen üyenin deftere giden döngü kanıtı — <see cref="BuildState"/>'in
    /// üç döngü alanı; bir sonraki Resolve'un tur 1 kararının (<see cref="CycleMemberNeed.Decide"/>) girdisi.</summary>
    private sealed record CycleMemberRecord(string? Term, IReadOnlyList<CycleReadSurface>? ReadSurfaces, string EngineFingerprint);

    /// <summary>[RESOLVE 3.4] Derlenen üyenin döngü kanıtı: bu koşunun terimi (Fast'te harita boş ⇒ null; terimsiz kayıt
    /// bir sonraki koşuda güvenilmez), son derlemesinde okuduğu kardeş yüzeyleri (<see cref="Flatten"/>) ve koşunun
    /// motor parmak izi.</summary>
    private static CycleMemberRecord CycleRecordOf(RunContext run, string projectId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? readStates) =>
        new(run.Incremental?.MemberTermById?.GetValueOrDefault(projectId), Flatten(readStates), run.EngineFingerprint);

    /// <summary>[RESOLVE 3.4] Okuma durumu (üretici → dosya → özet) → defterin kanonik listesi: önce Producer, sonra File
    /// (<see cref="StringComparer.OrdinalIgnoreCase"/>), her (Producer, File) tek girdi — kayıt eşitliği sıraya duyarlıdır
    /// (<see cref="BuildState.CycleReadSurfaces"/>). Okuma durumu yoksa (kanıtsız grup) null: bir sonraki koşu üyeyi
    /// "no trusted record" ile gerekli sayar. Okuma durumu kardeş haritasının her üreticisini taşır; izlenen dosyası
    /// kalmayan üretici kayda girmez ve karar o üyeyi bir sonraki koşuda gerekli sayar (güvenli taraf).</summary>
    private static IReadOnlyList<CycleReadSurface>? Flatten(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? readStates)
    {
        if (readStates is null) return null;
        return [.. readStates
            .OrderBy(producer => producer.Key, StringComparer.OrdinalIgnoreCase)
            .SelectMany(producer => producer.Value
                .OrderBy(file => file.Key, StringComparer.OrdinalIgnoreCase)
                .Select(file => new CycleReadSurface(producer.Key, file.Key, file.Value)))];
    }

    /// <summary>
    /// [cycle rounds] Bir SCC üyesinin NİHAİ sonucunu raporlar; tekil projeyle AYNI gövdeden
    /// (<see cref="ReportProjectResult"/>) geçer — sonuç olayı + decision.log + persist kararı + Complete.
    /// Turların hiçbirinde ara sonuç yayılmadığı için bu, o üye hakkında yayılan TEK sonuçtur ve
    /// <paramref name="totalDurationMs"/> turların TOPLAMIDIR.
    /// </summary>
    /// <param name="successIsTrusted">[D3] Üye OTURMUŞ: grup yakınsadı, ya da gerçek bir hükümle (NoProgress, CapReached)
    /// durdu ve üyenin okuduğu hiçbir grup-içi yüzey son tur sonunda bayat değildi — yeşil sonucu nihai API'lere bağlıdır
    /// ve persist edilir. Yalnız başarı tarafında okunur.</param>
    /// <param name="failureIsEvidence">[suçlu kırmızı] Üye, okuduğu her grup-içi yüzey NİHAİYKEN derleyici
    /// hatası verdi (NoProgress'in yüzey-kanıtlı yolu) — hatası sıradan bir Build hatası kadar kanıtlıdır ve
    /// kanıt kapısından geçer (satır kırmızı `failed`, defterde <see cref="BuildState.FailedSignature"/>).
    /// Kapının diğer şartları (derleyici çıkışı, bilinen imza, defter) yine kapının kendisindedir — timeout
    /// gibi bir gerekçe buradan true gelse bile kanıt olmaz.</param>
    /// <param name="cycleUnsettled">Tavana dayanmış grubun BAYAT (oturmamış) yeşil üyesi: derleme başarılı ama çıktı bir
    /// kuşak geride OLABİLİR. Dep-issue listesine sahte isim enjekte EDİLMEZ — ayrı bir bayrak taşınır.</param>
    /// <param name="cycle">[RESOLVE 3.4 · D3] Güvenilir başarının (oturmuş üye) döngü kanıtı (başarı persist'i yazar); diğer
    /// her yolda null.</param>
    /// <param name="dependencySurfaces">[D7-b] Güvenilir başarının grup DIŞI doğrudan bağımlılık yüzeyleri
    /// (<see cref="BuildState.DependencySurfaces"/>); diğer her yolda null.</param>
    private void ReportCycleMember(RunContext run, string projectId, BuildResult result, long totalDurationMs,
                                   string? failReason, DepIssueResult depIssues, bool successIsTrusted,
                                   bool failureIsEvidence, bool cycleUnsettled, CycleMemberRecord? cycle,
                                   IReadOnlyList<CycleReadSurface>? dependencySurfaces) =>
        ReportProjectResult(run, projectId, result, totalDurationMs, failReason, depIssues,
            // [D3] Güven sonuca göre AYRIŞIR: başarı yalnız oturmuşsa (Converged, ya da yakınsamayan grupta son turda
            // bayat olmayan üye), hata yalnız kanıtlı-umutsuzsa. İkisi tek ifadeden okunsaydı bayat yüzeyle patlayan
            // üye kanıtlı sayılır ya da oturmuş yeşil üye geçersizlenirdi.
            trustedResult: result == BuildResult.Succeeded ? successIsTrusted : failureIsEvidence,
            cycleUnsettled, failLogTail: null, cycle: cycle, dependencySurfaces: dependencySurfaces);

    /// <summary>
    /// [RESOLVE 3.4] Yakınsayan grubun hiç derlenmemiş (TAŞINAN) üyesini raporlar: tur 1'de gerekmedi (karar 2) ve tur
    /// sonlarında okuduğu hiçbir kardeş yüzeyi değişmedi (karar 3). <c>skipped — up to date</c> olayı ve decision.log
    /// satırı (ayrıntı <see cref="CycleDecisionLines.CarriedDetail"/>), defter yenilemesi
    /// (<see cref="RefreshBuildStateOnSkip"/>) ve <see cref="ReadySetScheduler.Complete"/> — Complete
    /// <c>finally</c> içinde TAM BİR KEZ (<see cref="ReportProjectResult"/>'ın sözleşmesi). Üye bu koşuda hiç invoke
    /// edilmediği için in-flight kaydı yoktur. Bin'deki kardeş kopyaları TAZELENMEZ (karar 5 — OutDir'e dokunulmaz).
    /// </summary>
    private void ReportCarriedCycleMember(RunContext run, string projectId, DepIssueResult depIssues)
    {
        try
        {
            ReportSkipped(run.Events, run.Logs, run.RunId, projectId, NameOf(run, projectId), SkipReasons.UpToDate,
                cycleUnconverged: false, detail: CycleDecisionLines.CarriedDetail);
            // [R3c2] Branch kesmesinden sonra hiçbir sonucun arkasında durulmaz (ReportProjectResult'taki kapı): kesilmiş
            // koşu taşınan üyenin kaydını da yenilemez — kayıt son güvenilir derlemenin olarak kalır.
            bool interrupted;
            lock (_gate) interrupted = _interrupted;
            if (!interrupted) RefreshBuildStateOnSkip(run, projectId, depIssues);
        }
        finally
        {
            run.Scheduler.Complete(projectId, BuildResult.Skipped);
        }
    }

    /// <summary>
    /// [RESOLVE 3.4 · karar 2 · D7] Derlenmeden "up to date" atlanan projenin defter kaydını yeniler — taşınan döngü üyesi
    /// ve yüzey kapısıyla atlanan proje AYNI gövdeden geçer: koşu başındaki kayıt (karar onu güvenilir buldu —
    /// <see cref="CycleMemberNeed.Decide"/> ya da <see cref="SurfaceGate.Decide"/>) <c>with</c> ile kopyalanır; YENİ bileşik
    /// imza, koşu zamanı, revizyon (<see cref="BuiltRevisionOf"/>) ve bu koşunun bağımlılık notu yazılır. Süre, içerik özeti,
    /// beslenen kopyalar, üç döngü alanı ve bağımlılık yüzeyleri AYNEN kalır: proje derlenmedi, kanıtı son güvenilir
    /// derlemenindir. Bağımlılık notu başarı persist'iyle aynı kuraldır (en az bir sorun ⇔ not + kökler); notlu kayıt bir
    /// sonraki koşuda güvenilmez. Persist I/O hatası koşuyu ÖLDÜRMEZ (warn-only).
    /// </summary>
    private void RefreshBuildStateOnSkip(RunContext run, string projectId, DepIssueResult depIssues) =>
        UpsertBuildState(run, projectId, (inc, signature) =>
        {
            // Kaydı koşu başında yoksa yenilenecek bir şey yok (atlanan proje güvenilir kayıtla atlanır).
            if (run.LedgerAtStart?.GetValueOrDefault(projectId) is not { } recorded) return null;
            var (builtCommit, branch) = BuiltRevisionOf(run, inc, projectId);
            IReadOnlyList<string>? depIssueRoots = DepIssueRootsOf(depIssues);
            return recorded with
            {
                BuiltSignature = signature, BuiltCommit = builtCommit, LastRunAt = DateTimeOffset.UtcNow, LastBranch = branch,
                DepIssue = depIssueRoots is not null, DepIssueRoots = depIssueRoots,
            };
        });

    /// <summary>
    /// [R3 final] Defter yazımının ORTAK gövdesi: başarı persist'i (<see cref="PersistBuildStateOnSuccess"/>) ve taşınan
    /// üyenin ve yüzey kapısıyla atlanan projenin defter yenilemesi (<see cref="RefreshBuildStateOnSkip"/>) buradan geçer — ön koşul ve warn-only
    /// <c>Upsert</c> (uyarı metni dahil) TEK yerde. Ön koşul: defter (<see cref="RunContext.StateStore"/>) ve bu proje
    /// için planlama imzası (<see cref="IncrementalPlan"/>) yoksa YAZILMAZ (testlerdeki basit planner → Incremental null →
    /// persist YOK, davranış nötr). <paramref name="create"/> yazılacak kaydı kurar (<c>null</c> ⇒ yazılacak bir şey
    /// yok). Persist I/O hatası koşuyu ÖLDÜRMEZ (warn-only).
    /// </summary>
    private void UpsertBuildState(RunContext run, string projectId, Func<IncrementalPlan, string, BuildState?> create)
    {
        if (run.StateStore is null || run.Incremental is not { } inc
            || !inc.SignatureById.TryGetValue(projectId, out var signature))
            return;
        if (create(inc, signature) is not { } state) return;
        try { run.StateStore.Upsert(state); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { console("warning: build-state could not be written (" + Path.GetFileNameWithoutExtension(projectId) + "): " + ex.Message); }
    }

    /// <summary>[design v1.14.0 §9] Kayda yazılan revizyon: HEAD ve branch ANA REPOYU anlatır; harici proje kendi çalışma
    /// kopyasının revizyonunu taşır (okunamadıysa boş) ve branch'i yazılmaz. Başarı persist'i ve taşınan üyenin defter
    /// yenilemesi aynı kuralı buradan okur.</summary>
    private static (string? Commit, string? Branch) BuiltRevisionOf(RunContext run, IncrementalPlan inc, string projectId) =>
        IsExternal(run, projectId)
            ? (inc.CommitByProjectId?.GetValueOrDefault(projectId), null)
            : (inc.HeadCommit, inc.Branch);

    /// <summary>[R3c3] Bağımlılık notunun defter karşılığı: en az bir sorun (<see cref="DepIssueResult.All"/>) ⇒ notun
    /// KÖKLERİ (kimlik; <see cref="DepIssueResult.RootIds"/>), sorun yoksa <c>null</c> — kayıt notsuz yazılır. Başarı
    /// persist'i (<see cref="ReportProjectResult"/>) ve taşınan üyenin defter yenilemesi aynı kuralı buradan okur.</summary>
    private static IReadOnlyList<string>? DepIssueRootsOf(DepIssueResult depIssues) =>
        depIssues.All.Count > 0 ? depIssues.RootIds : null;

    /// <summary>
    /// [cycle rounds] Tek bir MSBuild invoke'u — komut satırları, depIssue warn satırları dahil. Event YAYMAZ,
    /// Complete ÇAĞIRMAZ, BuildState PERSIST ETMEZ: bunlar çağıranın kararıdır. <paramref name="log"/> zaten
    /// açık gelir, ömrü çağıranındır — gerekçe gövdedeki [Kısıt 1] notunda.
    ///
    /// Neden ayrı: SCC tur döngüsü aynı projeyi birden çok kez invoke eder ama sonucu YALNIZ son turda
    /// raporlar. İki yol aynı invoke gövdesini paylaşmazsa komut satırı/log/retry davranışı sessizce
    /// ayrışırdı (kopya YASAK, CLAUDE.md).
    ///
    /// <para>[dalga görünürlüğü] Çağıran bir MSBuild slotu (<see cref="RunContext.InvokeSlots"/>) TUTARAK
    /// çağırır; bu metot slot almaz. Slotun sahibi, projeyi "derleniyor" ilan eden taraftır — ilan ile slot
    /// aynı elde durmazsa sıraya giren bir proje de derleniyor görünür.</para>
    /// </summary>
    /// <param name="observeLine">[okunan dosya kanıtı] MSBuild'in her satırı loga yazıldıktan sonra buna da
    /// verilir (döngü turu derleyicinin referanslarını buradan toplar). Pump thread'lerinden çağrılır; invoker
    /// döndükten sonra satır gelmez (<c>MsBuildInvoker</c>'ın mandalı).</param>
    private async Task<InvokeOutcome> InvokeOnceAsync(
        RunContext run, string projectId, DepIssueResult depIssues, ProjectLogFile log, CancellationToken ct,
        bool suppressRestore = false, Action<string>? observeLine = null)
    {
        // [PERF E3] Çözüm dizini hem isteğe hem restore kanıtına gider (paket klasörleri <solutionDir>\packages altında).
        string solutionDir = SolutionDirResolver.Resolve(projectId, run.SolutionRefs.GetValueOrDefault(projectId, []));
        // Proje kimliği (tam csproj yolu) derlenen dosyanın kendisidir; proje kendi (VS-parity) obj'inde derlenir.
        var request = new MsBuildInvokeRequest(
            ProjectId: projectId,
            Configuration: run.Configuration,
            SolutionDir: solutionDir,
            // Clean hiçbir şey derlemez: paket restore'u onun için anlamsız bir bekleme olurdu. [restore-once]
            // suppressRestore, SCC tur döngüsünün "bu üyenin bir önceki turu BAŞARILIYDI" bilgisidir: başarılı
            // invoke restore prologunu da içerir ve turlar arasında ne kaynak ne packages.config değişebilir —
            // aynı no-op restore'u her turda yeniden ödemek 7 üyeli gerçek bir grupta tur başına dakikalar
            // ölçüyordu. Başarısız üye yeniden restore ALIR (patlayan şey restore'un kendisi olabilir).
            // [PERF E3] packages.config projesi Build ve Cycles'ta restore kanıtı tatmin edildiyse prolog almaz;
            // Rebuild her zaman alır (NeedsPackagesRestore).
            NeedsRestore: !suppressRestore && run.MsBuildTarget != MsBuildTarget.Clean
                && NeedsPackagesRestore(run, projectId, solutionDir),
            Target: run.MsBuildTarget,
            // [WPF geçici assembly] Targets yolu koşuya toolset'ten gelir; komut satırı ve invoker AYNI isteği okur.
            CustomBeforeTargets: run.CustomBeforeTargetsPath);

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
        // Slot çağıranın elindedir ve timeout saati başlamadan ÖNCE alınmıştır (PerProjectTimeout invoker'ın
        // içinde kurulur) — sıra beklemek bir projenin süresine ya da zaman aşımına sayılmaz.
        var invoke = await run.Invoker.InvokeAsync(request, line =>
        {
            Emit(run, projectId, log, line);
            observeLine?.Invoke(line);
        }, ct);

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
            id => run.NodeById.TryGetValue(id, out var n) ? n.Name : id,
            // [tek proje] Kapsamlı koşuda hedefin derlenmeyen bayat bağımlılıkları da dep-issue'dur: düğüm
            // haritası yalnız hedefi taşır, o bağımlılıklar Completed'ta hiç yoktur — buradan gelmeseler
            // hedef bayat DLL'e karşı temiz bir başarı olarak persist edilirdi (gerekçe ProjectRunScope'ta).
            run.StaleDependenciesById?.GetValueOrDefault(projectId));
        run.DepIssuesById[projectId] = depIssues.RootIds; // birikim KİMLİK taşır (bkz. DepIssueTracker)
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
        // [tek proje] Bayat bağımlılık: bu koşuda derlenmedi, son bilinen çıktısı referans alındı. Döngü
        // üyesi için sebep farklıdır (turlar koşmadı), cümle de öyle — design §3.8'in iki uyarısı.
        foreach (var stale in depIssues.Stale)
            yield return stale.InCycle
                ? $"warning: {stale.Name} is in a dependency cycle and was not rebuilt — last known output referenced"
                : $"warning: {stale.Name} has pending changes and was not rebuilt in this run — last known output referenced";
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
        // Liste invoker'ın koşturduğuyla AYNI kaynaktan gelir (PlanFor): ilk log satırı gerçek komut satırıdır.
        // Argümanı burada ayrıca seçmek, yeni bir argümanda (ör. WPF targets'ı) log ile gerçek komutu ayrıştırırdı.
        var (restoreArgs, buildArgs) = MsBuildArguments.PlanFor(request);
        if (restoreArgs is not null) // restore ÖNCE koşar (bkz. MsBuildInvoker) — komut satırı da o sırada yazılır
            yield return WindowsCommandLine.Build(msbuildExePath, [.. restoreArgs]);
        yield return WindowsCommandLine.Build(msbuildExePath, [.. buildArgs]);
    }

    /// <summary>
    /// [Task 19] Bir proje BAŞARIYLA derlendiğinde <see cref="BuildState"/> persist eder — BİR SONRAKİ Build
    /// koşusu bunu okuyup (imza eşit + Succeeded ⇒ skip) incremental olur. Persist YALNIZ hem <see
    /// cref="RunContext.StateStore"/> hem de bu proje için non-null bir imza (<see cref="IncrementalPlan"/>)
    /// varsa yapılır (testlerdeki basit planner → Incremental null → persist YOK, davranış nötr). [A2] Çağıran
    /// (<see cref="ReportProjectResult"/>) depIssue TAŞIYAN success'i de buraya getirir — kayıt not + köklerle
    /// yazılır (<paramref name="depIssueRoots"/>; gerekçe orada); çağıranın kendi kapıları sonucun güvenilirliği
    /// (trustedResult) ve Clean'dir. §4: yalnız build-state.json'a yazılır, DLL/bin/obj'ye dokunulmaz. Persist
    /// I/O hatası run'ı ÖLDÜRMEZ (warn-only).
    /// </summary>
    /// <param name="depIssueRoots">Bu başarı BAŞARISIZ (ya da bayat bırakılmış) bağımlılıkların çıktısına link'liyse
    /// o KÖKLERİN proje kimlikleri; değilse <c>null</c>. Kayda not + kökler olarak yazılır;
    /// <see cref="Core.Planning.WillBuildEvaluator"/> onu görünce projeyi koşullu sayar
    /// (<see cref="WillBuildReason.WaitingForDependency"/>).</param>
    /// <param name="cycle">[RESOLVE 3.4] Güvenilir başarının (oturmuş üye) döngü kanıtı; null ⇒ üç döngü alanı NULL yazılır.</param>
    /// <param name="dependencySurfaces">[D6] Derlemenin bağlandığı doğrudan bağımlılık yüzeyleri (invoke'tan önce okunan,
    /// <see cref="DependencySurfacesOf"/>) — yüzey kapısının bir sonraki koşudaki tabanı; null ⇒ yüzey kanıtı yok.</param>
    private void PersistBuildStateOnSuccess(RunContext run, string projectId, long durationMs,
        IReadOnlyList<string>? depIssueRoots, CycleMemberRecord? cycle = null,
        IReadOnlyList<CycleReadSurface>? dependencySurfaces = null) =>
        UpsertBuildState(run, projectId, (inc, signature) =>
        {
            // [design v1.14.0 §9] HEAD ve branch ANA REPOYU anlatır. Harici bir proje kendi çalışma kopyasının
            // revizyonunu taşır (ExternalRevisionReader); okunamadıysa (çalışma kopyası yok ya da git hatası)
            // yuva BOŞ kalır —
            // yanlış bir reponun commit'ini göstermektense hiçbir şey göstermek doğrudur. Branch her koşulda ana
            // repoya aittir, harici kayda hiç yazılmaz.
            var (builtCommit, branch) = BuiltRevisionOf(run, inc, projectId);
            return new BuildState(projectId, signature, builtCommit, BuildResult.Succeeded,
                DateTimeOffset.UtcNow, branch, durationMs, DepIssue: depIssueRoots is not null,
                BuiltContent: inc.ContentById?.GetValueOrDefault(projectId), DepIssueRoots: depIssueRoots,
                // [Faz 3/Task 4 — spec 2026-09-18 §5.1] Bu derlemenin GERÇEKTEN güncellediği havuz kopyaları —
                // OutputsById'de kayıt yoksa (testlerdeki basit planner, kanıtsız proje) null (öğrenme yok).
                FedOutputs: OutputEvidence.LearnFedOutputs(inc.OutputsById?.GetValueOrDefault(projectId)),
                // [PERF E3] Restore'u koşan ya da kanıtla atlanan projenin karar anındaki packages.config özeti — bir
                // sonraki Build/Cycles koşusunun restore kanıtı. Kayıt yoksa (packages.config yok, özet okunamadı) null.
                PackagesConfigHash: run.PackagesConfigHashById.GetValueOrDefault(projectId),
                // [RESOLVE 3.4 · D3] Döngü kanıtı yalnız güvenilir (oturmuş) döngü üyesine yazılır. Döngü dışı her başarı (Build,
                // Rebuild, tek proje) taze kayıtla alanları NULL yazar — "mevcudu koru" DEĞİL: eski kanıt silinir, bir
                // sonraki Resolve üyeyi gerekli sayar (güvenli taraf).
                CycleMemberTerm: cycle?.Term, CycleReadSurfaces: cycle?.ReadSurfaces, CycleEngineFingerprint: cycle?.EngineFingerprint,
                // [D6] Bu derlemenin bağlandığı doğrudan bağımlılık yüzeyleri — yüzey kapısının karşılaştırma tabanı.
                DependencySurfaces: dependencySurfaces);
        });

    /// <summary>[tek proje · Clean] Başarılı bir <c>Clean</c>'den sonra projenin defter kaydını siler —
    /// gerekçe <see cref="BuildStateStore.Remove"/>'da. Defter I/O hatası koşuyu ÖLDÜRMEZ (warn-only), tıpkı
    /// persist ve invalidate yollarında olduğu gibi.</summary>
    private void ForgetBuildStateOnClean(RunContext run, string projectId)
    {
        if (run.StateStore is null) return;
        try { run.StateStore.Remove(projectId); }
        catch (Exception ex)
        { console("warning: build-state could not be cleared (" + Path.GetFileNameWithoutExtension(projectId) + "): " + ex.Message); }
    }

    /// <summary>
    /// [A2 fix-1][spec 2026-09-18 §1-14] Bir proje BAŞARISIZ bittiğinde stored <see cref="BuildState"/>'i
    /// GEÇERSİZLEŞTİRİR: <c>LastResult=Failed</c> yazılır, böylece <see cref="Core.Planning.WillBuildEvaluator"/>
    /// bir sonraki Build'de bu projeyi AYNI kaynakla "up to date" sayıp pre-skip EDEMEZ — kanıtsızsa
    /// <c>NeverBuilt</c>, kanıt bugünkü imzadaysa <c>LastFailed</c> okur. Tek istisna kaynağın geri alınmasıdır
    /// (spec §5.3): kanıt başka bir imzaya aitse ve kaynak son BAŞARILI imzaya (<c>BuiltSignature</c>) döndüyse
    /// karar <c>UpToDate</c>'tir — o imzanın son bilinen sonucu başarıdır. Yani "<c>LastResult != Succeeded</c>
    /// ⇒ derlenir" genel bir kural DEĞİLDİR. Önceki başarının çıktısı aracın kendisinin olduğundan defter
    /// kipinde okunur ve orada çıktının tarihi hatayı göremez (ARCHITECTURE §7.6): invalidasyonun tek yeri
    /// burasıdır.
    /// <para>
    /// <b>Yazım nedene göre AYRIŞIR.</b> Kanıt kararı bu metodun DIŞINDA, TEK yerde verilir
    /// (<see cref="FailureEvidenceSignature"/>: arkasında durulabilir sonuç + derleyici hatası + bilinen imza) ve
    /// buraya imza olarak gelir — AYNI değer App'e giden <see cref="ProjectFailedEvent.Evidence"/>'ı da belirler,
    /// böylece satır ile bir sonraki Sync ayrışamaz. Yakınsamayan bir SCC'nin (§8.8) bayat "yeşil" üyesi de bu metottan
    /// geçer; o kanıt SAYILMAZ. <b>Kanıtlıysa</b>: <see
    /// cref="BuildState.FailedSignature"/> planlamadaki imzayla, <see cref="BuildState.FailedAt"/> şimdiyle
    /// yazılır — kayıt yoksa <c>BuiltSignature: null</c> ile AÇILIR (hiç derlenmemiş bir proje ilk kez patladığında
    /// da kanıt kaybolmasın diye). İmzasız kanıt YOKTUR (<see cref="WillBuildEvaluator"/>'ın <c>LastFailed</c>'i
    /// imza eşitliğine bakar) — imza bilinmiyorsa kapı zaten kanıtsız der. <b>Kanıtsızsa</b> (timeout, stopped,
    /// invoke error, yakınsamayan grubun bayat yeşil üyesi) bugünkü davranış korunur: yalnız <c>LastResult</c>/
    /// <c>LastRunAt</c> güncellenir, eski <c>FailedSignature</c>/<c>FailedAt</c> null'a ÇEKİLİR (eski kanıt
    /// düşer — çıktı artık güvenilmez ama kaynağın bozuk olduğu KANITLI değil); kayıt yoksa <c>BuiltSignature:
    /// null</c>, <c>LastResult=Failed</c> ile AÇILIR — kaydı olmayan proje zaman kipindedir ve açılmasaydı yarıda
    /// kalan derlemenin taze çıktısı <c>BuiltOutside</c> okunabilirdi (<see
    /// cref="Core.State.BuildStateStore.InvalidateWithoutEvidence"/>).
    /// </para>
    /// <para>
    /// <b>Partial merge</b> (<see cref="Core.State.BuildStateStore.Upsert"/> kaydın TÜMÜNÜ değiştirir; bu yüzden mevcut
    /// kayıt okunur, yalnız ilgili alanlar <c>with</c> ile güncellenir) her iki yolda da geçerlidir:
    /// <see cref="BuildState.BuiltSignature"/>/<see cref="BuildState.BuiltCommit"/>/<see cref="BuildState.LastBranch"/>/
    /// <see cref="BuildState.LastDurationMs"/> DOKUNULMADAN korunur. Gerekçe: (1) imza, Fast (frozen-upstream)
    /// modda dependent'ların karşılaştırma tabanıdır — null'lanırsa bu projeye bağımlı HER proje de gereksizce
    /// dirty olurdu; (2) <c>LastDurationMs</c> son başarılı derlemenin tanı kaydıdır (ETA onu okumaz — ARCHITECTURE
    /// §8.4, §7.5); bir başarısızlığın (çoğu zaman erken patlayan) süresi o kaydın üzerine yazılmamalıdır.
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
    /// <param name="evidenceSignature">Kanıt kapısının cevabı (<see cref="FailureEvidenceSignature"/>) — kanıtlıysa
    /// hata anındaki imza, değilse <c>null</c>. Kapı burada YENİDEN hesaplanmaz: aynı değer App'e giden olayı da
    /// belirler (<see cref="ProjectFailedEvent.Evidence"/>).</param>
    private void InvalidateBuildStateOnFailure(RunContext run, string projectId, string? evidenceSignature)
    {
        if (run.StateStore is null) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            // [spec 2026-09-18 §5.5] Kanıtsız dal TEK yerdedir (çökme kurtarması da onu çağırır): kayıt yoksa açar.
            if (evidenceSignature is null) { run.StateStore.InvalidateWithoutEvidence(projectId, now); return; }

            run.StateStore.Load().TryGetValue(projectId, out var existing);
            var baseline = existing ?? new BuildState(projectId, BuiltSignature: null);
            run.StateStore.Upsert(baseline with
            {
                LastResult = BuildResult.Failed,
                LastRunAt = now,
                FailedSignature = evidenceSignature,
                FailedAt = now,
            });
        }
        catch (Exception ex)
        { console("warning: build-state could not be invalidated (" + Path.GetFileNameWithoutExtension(projectId) + "): " + ex.Message); }
    }

    /// <summary>
    /// [spec 2026-09-18 §5.5 · karar 12] Uçuş defterine (<c>run-inflight.json</c>) dokunan HER çağrının kapısı.
    /// Defter yoksa no-op; I/O hatası koşuyu DURDURMAZ, konsol uyarısıdır — çağıranların ikisi <c>finally</c>'dedir
    /// (sonuç raporu, koşu çıkışı) ve oradan kaçan bir istisna worker'ı öldürüp koşuyu asardı. Bedeli yalnız
    /// kurtarmanın o proje için eksik kalmasıdır; derlemenin kendisi etkilenmez.
    /// </summary>
    private void TrackInFlight(Action<InFlightLedger> write)
    {
        if (inFlight is null) return;
        try { write(inFlight); }
        catch (Exception ex)
        { console("warning: in-flight ledger could not be updated (" + inFlight.FilePath + "): " + ex.Message); }
    }

    /// <summary>
    /// [spec 2026-09-18 §1-14 · R-M4b] <b>Kanıt kapısının TEK yeri.</b> Bir başarısızlık, (1) sonucun arkasında
    /// durulabiliyorsa (<paramref name="trustedResult"/> — yakınsamayan bir SCC'de yalnız yüzeyleri oturmuşken
    /// patlayan kanıtlı-umutsuz üye için true gelir, bkz. <see cref="ReportCycleMember"/>), (2) nedeni derleyicinin
    /// kendi sıfır-dışı çıkışıysa (<see cref="FailureClassification.IsCompilerFailure"/> — timeout, stopped,
    /// invoke error değil) ve (3) planlamadaki imzası biliniyorsa (<c>run.Incremental.SignatureById</c> — imzasız
    /// kanıt YASAK, <see cref="Core.Planning.WillBuildEvaluator"/>'ın <c>LastFailed</c>'i imza eşitliğine bakar)
    /// ve (4) yazılacak bir defter varsa ve (5) koşunun hedefi DERLİYORSA (Build/Rebuild) KANITTIR. Dönen imza
    /// kanıtın kendisidir; <c>null</c> = kanıt yok. Hem defter yazımı hem App'e giden olay BUNU okur, ikisi
    /// ayrışamaz.
    /// <para>(5)'in gerekçesi: <c>msbuild /t:Clean</c> derleyiciyi hiç çağırmaz; sıfır-dışı çıkışı (kilitli
    /// dosya, erişim hatası) kaynağın derlenmediğini söylemez. Patlayan bir Clean kanıtsızdır — çıktı yarım
    /// silinmiş olabileceği için defter yine geçersizleşir, ama satır "bu kaynakta patladı" kırmızısı almaz.</para>
    /// </summary>
    private static string? FailureEvidenceSignature(RunContext run, string projectId, string? reason, bool trustedResult) =>
        run.StateStore is not null
        && (run.MsBuildTarget is MsBuildTarget.Build or MsBuildTarget.Rebuild)
        && trustedResult
        && FailureClassification.IsCompilerFailure(reason)
        && run.Incremental is { } inc
        && inc.SignatureById.TryGetValue(projectId, out var signature)
            ? signature
            : null;

    /// <summary>[R3 final] <c>invoke error: …</c> gerekçe önekinin TEK literal'i: <see cref="InvokeErrorReason"/> yazar;
    /// Acceptance sınıflandırıcısı (orchestrator kaynaklı hata sinyali) aynı sabiti okur — kopya YASAK. Serbest metindir,
    /// derleyici kanıtı DEĞİLDİR (<see cref="FailureClassification"/>).</summary>
    public const string InvokeErrorPrefix = "invoke error: ";

    /// <summary>[R3 final] <c>group start failed: …</c> gerekçe önekinin TEK literal'i (<see cref="GroupStartErrorReason"/>);
    /// <see cref="InvokeErrorPrefix"/> ile aynı kural: kanıt değil, orchestrator kaynaklı hata sinyali.</summary>
    public const string GroupStartFailedPrefix = "group start failed: ";

    /// <summary>[R3c3] Beklenmeyen bir istisnanın başarısızlık gerekçesi — <c>invoke error: …</c> metninin TEK sahibi: tekil
    /// proje ve SCC grubu aynı yerden yazar. Serbest metindir, derleyici kanıtı DEĞİLDİR (<see cref="FailureClassification"/>).
    /// <see cref="AggregateException"/> sarmalı (ör. Parallel.ForEach) ilk iç istisnaya AÇILIR: sarmalın "One or more errors
    /// occurred" metni asıl nedeni gizlerdi.</summary>
    private static string InvokeErrorReason(Exception ex) => InvokeErrorPrefix + FirstInner(ex).Message;

    /// <summary>[R3c3] Grubun ilk dispatch'inden ÖNCE (yüzey hash'i, tur 1 seçimi, başlık satırları) fırlayan istisnanın
    /// gerekçesi — henüz hiçbir şey invoke edilmemişken "invoke error" yanıltırdı. Sahibi <see cref="InvokeErrorReason"/> ile
    /// AYNI yerdir (aynı açma kuralı, aynı biçim); kanıt DEĞİLDİR.</summary>
    private static string GroupStartErrorReason(Exception ex) => GroupStartFailedPrefix + FirstInner(ex).Message;

    /// <summary>[R3c3] <see cref="AggregateException"/> sarmalını (iç içe olsa da) ilk iç istisnaya açar; sarmal değilse
    /// istisnanın kendisi. Paralel döngü birden çok iş parçacığında AYNI nedenle patlayabilir — ilki nedeni anlatır.</summary>
    private static Exception FirstInner(Exception ex) =>
        ex is AggregateException { InnerExceptions.Count: > 0 } aggregate ? FirstInner(aggregate.InnerExceptions[0]) : ex;

    private string ReasonFor(MsBuildInvokeResult invoke)
    {
        // Hard stop ÖNCE bakılır: TerminateJobObject child'ı öldürdüğünde invoke sıradan bir "exit N" gibi döner
        // (OperationCanceledException DEĞİL) — bu, kullanıcının bilinçli Stop'udur, projenin hatası değil.
        lock (_gate)
        {
            if (_stopKind == StopKind.Hard) return FailureReasons.Stopped;
        }
        if (invoke.TimedOut) return "timeout";
        if (invoke.Killed) return FailureReasons.Stopped;
        // [spec 2026-09-18 §1-14] Önek TEK kaynaktan: FailureClassification.IsCompilerFailure aynı sabiti okur —
        // literal iki yerde tanımlanmaz (kopya YASAK, CLAUDE.md).
        return string.Format(CultureInfo.InvariantCulture, "{0}{1}", FailureClassification.ExitPrefix, invoke.ExitCode);
    }

    // [I2-K2/S2] Legacy restore sinyali: csproj'un YANINDA packages.config (yolu; yoksa null). bin/OutDir'e
    // BAKILMAZ [§4].
    private static string? PackagesConfigOf(string projectId)
    {
        string? dir = Path.GetDirectoryName(Path.GetFullPath(projectId));
        string? path = dir is null ? null : Path.Combine(dir, "packages.config");
        return path is not null && File.Exists(path) ? path : null;
    }

    /// <summary>
    /// [PERF E3] Bir packages.config projesinin bu invoke'ta restore prologu alıp almayacağı. Rebuild (toparlanma
    /// yolu) kanıta bakmadan restore eder. Build ve Cycles, koşu başındaki defterin özeti bugünkü içerikle aynıysa ve
    /// listelenen paketler kuruluysa (klasör + .nupkg) restore'u atlar ve nedenini decision.log'a yazar — karar da metin
    /// de Core'dadır (<see cref="RestoreEvidence"/>); burada yalnız defter ve mod bağlanır. Restore koşsa da
    /// atlansa da karar anındaki özet <c>RunContext.PackagesConfigHashById</c>'e düşer; başarı onu deftere yazar.
    /// Kanıt yalnız içeriktir, tarih karara girmez.
    /// </summary>
    private bool NeedsPackagesRestore(RunContext run, string projectId, string solutionDir)
    {
        if (PackagesConfigOf(projectId) is not { } packagesConfig) return false;
        string? recorded = run.LedgerAtStart?.GetValueOrDefault(projectId)?.PackagesConfigHash;
        if (run.Mode != RunMode.Rebuild
            && RestoreEvidence.IsSatisfied(packagesConfig, solutionDir, recorded, out int present))
        {
            run.PackagesConfigHashById[projectId] = recorded!;
            Decide(run.Logs, RestoreEvidence.SkippedLine(NameOf(run, projectId), present));
            return false;
        }
        // Özet okunamadıysa (dosya kilitli ya da kayboldu) kayıt düşer: başarı null yazar, sonraki koşu restore eder.
        if (RestoreEvidence.HashOf(packagesConfig) is { } current) run.PackagesConfigHashById[projectId] = current;
        else run.PackagesConfigHashById.TryRemove(projectId, out _);
        return true;
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
        // [T54] projectId → depIssues birikimi (PlanAndRunAsync'te kurulur; koşu başına TEK birikim, ömrü o
        // koşuyla sınırlıdır). ConcurrentDictionary: N worker aynı anda FARKLI key'lere yazar, birbirinin key'ini okur.
        ConcurrentDictionary<string, IReadOnlyList<string>> DepIssuesById,
        // [Task 19] projectSucceeded → BuildState persist hedefi (null ⇒ persist YOK); imza/HEAD/branch kaynağı.
        // [A2 fix-1] AYRICA projectFailed → mevcut kaydın LastResult'ı Failed'a çekilir (stale pre-skip'i keser).
        BuildStateStore? StateStore,
        IncrementalPlan? Incremental,
        // [cycle rounds] SCC üyelik haritası — <see cref="ReadySetScheduler"/>'a verilenin AYNI örneği (null ⇒
        // plan'da SCC yok ya da kill switch kapalı; o zaman worker yalnız tekil proje yolunu kullanır).
        CycleGroups? Groups,
        // [koşullu yeniden derleme] Sırası geldiğinde ConditionalRebuild.Decide'dan geçecek projeler.
        IReadOnlySet<string> ConditionalIds,
        // [koşullu yeniden derleme] Koşu BAŞINDA okunan defter (null ⇒ store yok): koşullu projenin kökleri ve bu
        // koşuda derlenmeyen kökün son sonucu buradan okunur. Koşu içi persist'ler bu örneğe yansımaz — kasıtlı,
        // bu koşuda derlenen kökün sonucu zaten scheduler'dan okunur.
        IReadOnlyDictionary<string, BuildState>? LedgerAtStart,
        // [seviyeli turlar] Koşunun MSBuild-child tavanı: her invoke (tekil worker yolu da, bir SCC seviyesinin
        // eşzamanlı üyeleri de) bu semafordan slot alır — worker sayısı + seviye genişliği hiçbir bileşimde
        // parallelism'i aşamaz. [dalga görünürlüğü] Slotu "derleniyor" ilanını yapan taraf tutar: worker tekil
        // projeyi dispatch'ten ÖNCE alıp sonucundan SONRA bırakır, SCC üyesi ilanından önce alıp "bitti"
        // ilanından sonra bırakır — ilan edilen derleme sayısı hiçbir anda slot sayısını aşamaz.
        SemaphoreSlim InvokeSlots,
        // [tek proje] projectId → bu koşuda derlenmeyen bayat bağımlılıkları (yalnız kapsamlı koşuda, yalnız
        // hedef için dolu; null ⇒ tam koşu). ComputeDepIssues bunu DepIssueTracker'a geçirir.
        IReadOnlyDictionary<string, IReadOnlyList<StaleDependency>>? StaleDependenciesById = null,
        // [tek proje] Bu koşunun MSBuild hedefi — yalnız satır menüsünün Rebuild'i Build'den ayrılır (§3.8).
        MsBuildTarget MsBuildTarget = MsBuildTarget.Build,
        // [WPF geçici assembly] Targets dosyasının tam yolu (toolset'ten); null ⇒ build komut satırına girmez.
        string? CustomBeforeTargetsPath = null,
        // [PERF E3] Koşunun modu — restore kararı onu okur: Rebuild (toparlanma yolu) paket kanıtına bakmadan her
        // packages.config projesini restore eder; Build ve Cycles kanıt tatmin edildiyse atlar. Tek kuruluş yeri
        // (Mode: cmd.Mode) modu her zaman geçer; varsayılan GÜVENLİ yöndedir (kanıta bakılmaz, restore koşar) —
        // yalnız Mode'u unutan gelecekteki bir kuruluş yerini güvenli tarafta tutar.
        RunMode Mode = RunMode.Rebuild,
        // [D7] Sırası gelince yüzey kapısından geçecek projeler (IncrementalPlan.SurfaceCandidateIds) — yalnız kapının
        // uygulandığı koşuda dolu (SurfaceGate.AppliesTo: defteri dinleyen tam koşu); null ⇒ kapı yok. Mode gibi SONDA ve
        // default'lu: eski kuruluş yerleri (testler) değişmez.
        IReadOnlySet<string>? SurfaceCandidateIds = null)
    {
        /// <summary>[D9] Bağımlılık → bu koşuda okunan ŞİMDİKİ yüzey özeti (kanıt dosyasından, ApiSurfaceHash); tembel ve koşu
        /// boyunca sabit: bağımlılık dispatch anında terminaldir, çıktısı diskte nihaidir. Yüzey yoksa/okunamıyorsa null.</summary>
        public ConcurrentDictionary<string, string?> SurfaceById { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>[PERF E3] projectId → bu koşuda restore'u koşan ya da kanıtla atlanan projenin, karar ANINDA
        /// okunan packages.config özeti. Başarı persist'i (<c>PersistBuildStateOnSuccess</c>) onu deftere yazar:
        /// derleme sürerken dosya değişse bile defter restore kararının gördüğü içeriği anlatır.</summary>
        public ConcurrentDictionary<string, string> PackagesConfigHashById { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>[RESOLVE 3.4 · karar 6] Koşunun motor parmak izi: her derleme isteğine giren AYNI MSBuild.exe yolu
        /// (<see cref="MsBuildExePath"/>) ve AYNI targets yolundan (<see cref="CustomBeforeTargetsPath"/> — toolset'in
        /// <c>MsBuildToolset.CustomBeforeTargetsPath</c>'i) türetilir; ikinci bir yol hesabı yok. Yalnız derleme komut
        /// satırını kapsar (restore dışarıda). İlk döngü grubunda bir kez hesaplanır (dosya sürümü okuması); eşzamanlı
        /// iki grup aynı değeri üretir. Argüman listesini koordinatör SEÇMEZ: hesap Core'daki
        /// <c>EngineFingerprint.ForToolset</c>'tedir (guard: <c>MsBuildArgumentsTests</c>).</summary>
        public string EngineFingerprint => _engineFingerprint ??=
            Core.MsBuild.EngineFingerprint.ForToolset(MsBuildExePath, CustomBeforeTargetsPath);

        private string? _engineFingerprint;
    }

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
