using System.Collections.Concurrent;
using System.Globalization;
using System.Threading.Channels;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
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
/// <param name="planner">(root, configuration) → <see cref="RunPlan"/>. Core'un planlama pipeline'ı; senkron ve
/// I/O yapar, bu yüzden run'ın arka plan task'ından çağrılır (IPC dispatch loop'u bloklanmaz).</param>
/// <param name="msbuildFactory">MSBuild takımını (ham invoker + exe yolu) LAZY çözer: vswhere/VS yoksa Supervisor
/// yine ayağa kalkar, hata ancak <c>startRun</c>'da <c>error(msbuildNotFound)</c> olarak bildirilir.</param>
/// <param name="logFactory">Run başına TEK <see cref="RunLogWriter"/> üretir; Continue AYNI writer'ı (aynı run
/// dizinini) kullanır — log dikişi bozulmaz.</param>
/// <param name="nowMs">MONOTONİK zaman kaynağı (üretimde <c>Environment.TickCount64</c>); duvar saati
/// KULLANILMAZ — geri atlarsa elapsed negatife düşerdi.</param>
/// <param name="console">Konsol (stderr) uyarı/özet kanalı. stdout YALNIZ NDJSON'dır [D4], bu yüzden buradan
/// asla stdout'a yazılmaz.</param>
/// <param name="worktreeObjRootResolver">
/// [I2-K2/It-3 Task 10] <c>cmd.UseWorktree</c>=true iken bu run için kullanılacak worktree kökünü döner (null
/// dönerse in-place gibi davranılır — obj izole EDİLMEZ). Worktree KÖKÜNÜN gerçekten hazırlanması
/// (<c>WorktreeManager.PrepareWorktreeAsync</c>) bu run akışına HENÜZ bağlı değildir (<c>planner</c> yalnız
/// <c>cmd.RootPath</c>/<c>cmd.Configuration</c> alır) — bu yüzden parametre isteğe bağlıdır ve verilmezse
/// (varsayılan <c>null</c>) mevcut davranış (her zaman in-place obj) korunur. Verildiğinde, dönen kök
/// <see cref="Core.MsBuild.WorktreeObjPathResolver.Resolve"/> ile proje-Id başına izole bir
/// <c>BaseIntermediateOutputPath</c>'e çevrilir — obj PAYLAŞILMAZ (bayat-obj zehri, SPIKE-proven
/// OSYS.Types.NewSales.Print vakası).
/// </param>
public sealed class RunCoordinator(
    Func<StartRunCommand, RunPlan> planner,
    Func<CancellationToken, Task<MsBuildToolset>> msbuildFactory,
    Func<DateTimeOffset, RunLogWriter> logFactory,
    NdjsonWriter writer,
    JobObject innerJob,
    Func<long> nowMs,
    Action<string> console,
    Func<StartRunCommand, string?>? worktreeObjRootResolver = null,
    BuildStateStore? stateStore = null) : IDisposable
{
    private readonly object _gate = new();

    // --- run yaşam döngüsü (hepsi _gate altında) ---
    private bool _runActive;            // startRun slotu dolu (planlama dahil) — A6
    private bool _finishing;            // sonuç olayları yazılmaya başladı → Stop artık sahiplenilemez
    private Task _runTask = Task.CompletedTask;
    private StopKind? _stopKind;        // null = stop istenmedi; Hard, Graceful'u EZER (geri alınmaz)
    private bool _stopAcked;            // runStopped yazıldı mı — TryRequestStop true dedi ise ACK BORCU vardır
    private ReadySetScheduler? _scheduler;
    private WakeSignal? _wake;

    // --- Continue için devredilen state (run'lar ARASINDA yaşar) ---
    private RunPlan? _plan;
    private string? _root;
    private RunLogWriter? _logs;
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
            if (_disposed)
                rejection = new ErrorEvent("runInProgress", "Supervisor kapanıyor — yeni run kabul edilmiyor.");
            else if (_runActive)
                rejection = new ErrorEvent("runInProgress", $"Zaten bir run koşuyor — '{cmd.RunId}' reddedildi.");
            else if (cmd.Mode == RunMode.Continue && !IsResumableForLocked(cmd.RootPath))
                rejection = new ErrorEvent("noResumableRun", $"'{cmd.RootPath}' için sürdürülebilir bir run yok.");
            else if (cmd.Mode == RunMode.RetryFailed && !IsRetryableForLocked(cmd.RootPath))
                rejection = new ErrorEvent("noResumableRun", $"'{cmd.RootPath}' için retry edilecek failed proje yok.");
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
    /// <para><b>Graceful:</b> yalnız <see cref="ReadySetScheduler.RequestStop"/> — yeni dispatch yok, in-flight
    /// <c>MSBuild.exe</c> child'ları post-build copy DAHİL kendi tamamlanmalarını yapar (ortak çıktı dizini
    /// yarım yazılmış kalmaz). <b>Hard:</b> inner Job ANINDA terminate edilir; in-flight projeler
    /// <c>projectFailed("stopped")</c> raporlanır. Terminate edilmiş Job yeni process kabul ettiği için ikisi de
    /// Continue'ya açıktır.</para>
    /// </summary>
    public bool TryRequestStop(StopKind kind)
    {
        lock (_gate)
        {
            if (!_runActive || _finishing) return false;
            _stopKind = kind == StopKind.Hard ? StopKind.Hard : _stopKind ?? StopKind.Graceful; // Hard geri alınmaz
            if (kind == StopKind.Hard) innerJob.Terminate();
            _scheduler?.RequestStop(); // null ise plan hâlâ kuruluyor — kurulur kurulmaz _stopKind okunup uygulanır
            _wake?.WakeAll();          // parked worker'lar IsDone'ı yeniden değerlendirsin
            return true;
        }
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
        ReadySetScheduler scheduler;
        RunClock clock;
        long elapsedAtStart;
        ConcurrentDictionary<string, IReadOnlyList<string>> depIssuesById;
        ConcurrentDictionary<string, byte> stoppedFailedIds;
        // [Task 19] Build modunda incremental olarak "up to date" (WillBuild==false, cycle DIŞI) pre-skip edilen
        // projeler — cycle pre-skip'i gibi construction anında Skipped sayılır (dependent'ları için resolved),
        // ProjectSkippedEvent("skipped — up to date") ile raporlanır. Rebuild/Continue/RetryFailed'de boş kalır.
        var upToDateSkips = new List<(string ProjectId, string Reason)>();

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
            scheduler = new ReadySetScheduler(runPlan.Plan, effectiveSnapshot);
            elapsedAtStart = snapshot.ElapsedMs;
            clock = new RunClock(nowMs, snapshot.ElapsedMs);
        }
        else
        {
            try { runPlan = planner(cmd); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            { events.TryWrite(new ErrorEvent("planFailed", ex.Message)); return; }

            lock (_gate)
            {
                _logs?.Dispose(); // terk edilmiş (artık sürdürülmeyecek) önceki run'ın writer'ı
                _snapshot = null;
                _plan = runPlan;
                _root = Canonical(cmd.RootPath);
                logs = _logs = logFactory(DateTimeOffset.Now);
                _lastRunDirectory = logs.RunDirectory;
                depIssuesById = _depIssuesById = new ConcurrentDictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase); // [T54] taze run → taze birikim
                stoppedFailedIds = _stoppedFailedIds = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase); // [Task-13] taze run → taze birikim
            }
            // [Task 19] Yalnız Build modunda: planlayıcının hesapladığı WillBuild==false (ve cycle DIŞI) projeler
            // "up to date" olarak pre-skip edilir — scheduler'a Skipped tohumlanır (dependent'ları için resolved),
            // dispatch edilmezler. Rebuild HER ŞEYİ derler (tohum yok, mevcut davranış). Cycle üyeleri seed'e
            // GİRMEZ → ctor onları "in dependency cycle" reason'ıyla ayrı pre-skip eder (reason karışmaz).
            if (cmd.Mode == RunMode.Build)
            {
                var seed = new Dictionary<string, BuildResult>(StringComparer.OrdinalIgnoreCase);
                foreach (var n in runPlan.Plan.Nodes)
                    if (n.WillBuild == false && !n.InCycle)
                    {
                        seed[n.Id] = BuildResult.Skipped;
                        upToDateSkips.Add((n.Id, "skipped — up to date"));
                    }
                scheduler = seed.Count > 0
                    ? new ReadySetScheduler(runPlan.Plan, new RunSnapshot(seed, [], 0))
                    : new ReadySetScheduler(runPlan.Plan);
            }
            else
            {
                scheduler = new ReadySetScheduler(runPlan.Plan);
            }
            elapsedAtStart = 0;
            clock = new RunClock(nowMs);
        }

        MsBuildToolset toolset;
        try { toolset = await msbuildFactory(ct); }
        catch (MsBuildResolveException ex)
        { events.TryWrite(new ErrorEvent("msbuildNotFound", ex.Message)); return; }

        // [I2-K2/Task 10] cmd.UseWorktree=false → HER ZAMAN null (in-place, VS-parity, mevcut davranış). true
        // iken resolver YOKSA (Program.cs henüz worktree hazırlamıyor) yine null'a düşer — obj izolasyonu ancak
        // resolver GERÇEK bir worktree kökü döndürdüğünde devreye girer.
        string? worktreeObjRoot = cmd.UseWorktree ? worktreeObjRootResolver?.Invoke(cmd) : null;

        // [T72/Task 14] SPIKE S2 — bayat-obj (yabancı-TFM restore artığı) teşhisi YALNIZ taze (Rebuild/Build)
        // segmentte VE in-place (worktreeObjRoot null — izole obj YOK) projeler için tetiklenir: Continue/RetryFailed
        // AYNI obj üstünde devam eder (yeniden teşhis gerekmez), worktree run'ları zaten PAYLAŞILMAYAN izole obj
        // kullanır (bayat-obj zehri worktree'de oluşamaz). onRetry ile AYNI ikili-yazım deseni: hem decision.log
        // hem konsol. Dokunmaz, yalnız warn (StaleObjRunStartWarner ASLA fırlatmaz).
        if (cmd.Mode is not (RunMode.Continue or RunMode.RetryFailed) && worktreeObjRoot is null)
            StaleObjRunStartWarner.WarnStaleObj(runPlan.Plan.Nodes, line => { Decide(logs, line); console(line); });

        int parallelism = Math.Max(1, cmd.Parallelism);
        var wake = new WakeSignal();
        lock (_gate)
        {
            _scheduler = scheduler;
            _wake = wake;
            if (_stopKind is not null) scheduler.RequestStop(); // plan kurulurken gelmiş Stop
        }

        clock.Start();
        var plan = runPlan.Plan;
        var nodeById = plan.Nodes.ToDictionary(n => n.Id, StringComparer.OrdinalIgnoreCase);
        events.TryWrite(new RunStartedEvent(cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism,
            plan.Configuration, elapsedAtStart));
        // [Task 17] runStarted'dan HEMEN SONRA, ilk projectStarted/projectSkipped'ten ÖNCE: App'in Projects
        // listesini will-build önizlemesiyle pre-populate edebilmesi için. WillBuild alanı doğrudan plan'ın
        // düğümlerinden (BuildPreview/IncrementalPlanner'ın doldurduğu — henüz run akışına tam bağlanmadıysa null)
        // taşınır; burada AYRICA hesaplanmaz.
        events.TryWrite(new BuildPreviewEvent(
            [.. plan.Nodes.Select(n => new BuildPreviewItem(n.Id, n.Name, n.WillBuild))]));
        // v7Δ-7: konsolda solution-level msbuild izlenimi verilmez — motorun gerçeği proje-başına shell-out'tur,
        // gerçek komut satırları proje loglarındadır.
        console(string.Format(CultureInfo.InvariantCulture,
            "Run {0} ({1}): {2} proje, {3} worker, {4} — her proje ayrı bir derleyici child process'i olarak derlenir; komut satırları proje loglarında.",
            cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism, plan.Configuration));
        Decide(logs, string.Format(CultureInfo.InvariantCulture,
            "run {0} başladı: mode={1} projeler={2} parallelism={3} configuration={4} elapsedAtStart={5}ms",
            cmd.RunId, cmd.Mode, plan.Nodes.Count, parallelism, plan.Configuration, elapsedAtStart));

        // runStarted yazıldı: buradan SONRA hangi yoldan çıkılırsa çıkılsın (beklenmeyen exception dahil)
        // kapanış olayları TAM OLARAK BİR KEZ yazılır — aksi halde App'in run'ı sonsuza dek "koşuyor" kalırdı.
        try
        {
            // Cycle üyeleri (construction anında Skipped) — resume edilmiş scheduler'ın PreSkipped'i BOŞTUR,
            // bu yüzden Continue'da tekrar yazılmazlar (yalnız snapshot onları taşımıyorsa savunmacı olarak yazılır).
            foreach (var (projectId, reason) in scheduler.PreSkipped)
            {
                events.TryWrite(new ProjectSkippedEvent(cmd.RunId, projectId, reason));
                Decide(logs, $"{nodeById[projectId].Name}: atlandı — {reason}");
            }
            // [Task 19] Build modunda incremental "up to date" skip'ler (cycle pre-skip ile AYNI konumda, ilk
            // dispatch'ten ÖNCE): dependent'ları için scheduler'da zaten Skipped/resolved tohumlandı.
            foreach (var (projectId, reason) in upToDateSkips)
            {
                events.TryWrite(new ProjectSkippedEvent(cmd.RunId, projectId, reason));
                Decide(logs, $"{nodeById[projectId].Name}: atlandı — {reason}");
            }

            var run = new RunContext(
                cmd.RunId, plan.Configuration, runPlan.SolutionRefs, nodeById,
                scheduler, wake, logs, events,
                // Retry politikası Core'un [T8]; burada yalnız run'a bağlanır: onRetry hem decision.log'a hem konsola.
                new RetryingMsBuildInvoker(toolset.Invoker, RetryingMsBuildInvoker.DefaultBackoff,
                    (delay, token) => Task.Delay(delay, token),
                    onRetry: message => { Decide(logs, message); console(message); }),
                toolset.MsBuildExePath,
                worktreeObjRoot,
                depIssuesById, // [T54]
                stoppedFailedIds, // [Task-13]
                stateStore, // [Task 19] projectSucceeded → BuildState persist (null ⇒ persist YOK, mevcut test davranışı)
                runPlan.Incremental); // [Task 19] imza + HEAD + branch (persist için)

            var workers = Enumerable.Range(0, parallelism)
                .Select(_ => Task.Run(() => WorkerAsync(run, ct), CancellationToken.None))
                .ToArray();
            try { await Task.WhenAll(workers); }
            catch (Exception ex)
            {
                // Worker'lar normalde fırlatmaz (her proje kendi sonucunu raporlar). Yine de fırlarsa: run ASILI
                // KALMAZ — aşağıdaki finally snapshot alıp runCompleted yazar; kalanlar Queued olarak raporlanır.
                Decide(logs, "worker beklenmedik şekilde sonlandı: " + ex.Message);
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
                "run {0} bitti: outcome={1} succeeded={2} failed={3} skipped={4} queued={5} duration={6}ms depIssues={7}",
                cmd.RunId, outcome, succeeded, failed, skipped, snapshotAtEnd.Queued.Count, clock.ElapsedMs, depIssueCount));

            // [Task-13] Continue backlog'u (Stopped + Queued>0) VEYA en az bir Failed proje varsa (RetryFailed'a
            // açık — bkz. HasRetryableRunLocked) plan/logs/birikimler devredilir; ikisi de yoksa (run tamamen
            // temiz bitti) her şey temizlenir — aksi halde RetryFailed'ın "yeniden derlenecek" bir kümesi olmaz.
            bool resumable = (outcome == RunOutcome.Stopped && snapshotAtEnd.Queued.Count > 0) || failed > 0;
            lock (_gate) _snapshot = resumable ? snapshotAtEnd : null;
            if (!resumable)
            {
                // [Kısıt 1] RunLogWriter ancak TÜM worker'lar join olduktan sonra dispose edilir.
                lock (_gate) { _logs = null; _plan = null; _root = null; _depIssuesById = null; _stoppedFailedIds = null; } // [T54/Task-13]
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
        catch (IOException ex) { console("decision.log yazılamadı: " + ex.Message); }
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
            try { await BuildProjectAsync(run, projectId, ct); }
            finally { run.Wake.WakeAll(); } // Complete edildi (ya da patladı) → parked worker'lar yeniden baksın
        }
    }

    /// <summary>Tek projenin tüm yaşam döngüsü. <see cref="ReadySetScheduler.Complete"/> her yoldan TAM BİR KEZ çağrılır.</summary>
    private async Task BuildProjectAsync(RunContext run, string projectId, CancellationToken ct)
    {
        // Dispatch ile Complete arasındaki HER ŞEY try/finally içinde: buradan fırlayan bir exception Complete'i
        // atlarsa proje sonsuza dek in-flight kalır (IsDone asla true olmaz) ve run ASILIR. Bu yüzden ad araması
        // da fırlatmayan biçimde yapılır (id her zaman plan'da vardır — scheduler aynı plan'dan sürülür).
        string name = run.NodeById.TryGetValue(projectId, out var node) ? node.Name : projectId;
        var result = BuildResult.Failed;

        // [T54] Dispatch anında TÜM bağımlılıklar zaten terminaldir (ReadySetScheduler'ın resolved-gate'i,
        // IsReadyLocked) — bu yüzden depIssues burada, invoke'tan ÖNCE, güvenle hesaplanıp HEM warn satırlarına
        // HEM olaya (event) HEM de birikime (bu projenin kendi dependent'ları miras alabilsin diye) yazılabilir.
        var depIssues = DepIssueTracker.Compute(
            node?.Dependencies ?? [],
            run.Scheduler.Completed,
            run.DepIssuesById,
            id => run.NodeById.TryGetValue(id, out var n) ? n.Name : id);
        run.DepIssuesById[projectId] = depIssues.All;
        IReadOnlyList<string>? depIssuesForEvent = depIssues.All.Count > 0 ? depIssues.All : null;

        try
        {
            run.Events.TryWrite(new ProjectStartedEvent(run.RunId, projectId, name));
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

            MsBuildInvokeResult invoke;
            // [Kısıt 1] Proje logu YALNIZCA bu projenin invoke'u bittikten sonra dispose edilir (dispose sonrası
            // AppendLine fırlatır — satır sessizce düşmez).
            using (var log = run.Logs.OpenProjectLog(projectId))
            {
                // v7Δ-7: proje logunun İLK satırı, bu proje için çalıştırılacak GERÇEK MSBuild komut satırıdır —
                // depIssue uyarıları bu invaryantı BOZMAZ, komut satır(lar)ından SONRA, gerçek derleme çıktısından
                // ÖNCE (log başı) yazılır [T54].
                foreach (string commandLine in CommandLines(request, run.MsBuildExePath))
                    Emit(run, projectId, log, commandLine);
                foreach (string warnLine in DepIssueWarnLines(depIssues))
                    Emit(run, projectId, log, warnLine);
                invoke = await run.Invoker.InvokeAsync(request, line => Emit(run, projectId, log, line), ct);
            }

            if (invoke.ExitCode == 0 && !invoke.TimedOut && !invoke.Killed)
            {
                result = BuildResult.Succeeded;
                run.StoppedFailedIds.TryRemove(projectId, out _); // [Task-13] artık Failed değil — eski işaret geçersiz
                PersistBuildStateOnSuccess(run, projectId, invoke.DurationMs); // [Task 19] sonraki Build incremental olsun
                run.Events.TryWrite(new ProjectSucceededEvent(run.RunId, projectId, invoke.DurationMs, depIssuesForEvent));
                Decide(run.Logs, string.Format(CultureInfo.InvariantCulture,
                    "{0}: başarılı ({1}ms)", name, invoke.DurationMs));
            }
            else
            {
                string reason = ReasonFor(invoke);
                MarkStoppedFailed(run, projectId, reason); // [Task-13] Continue'un torn-DLL guard'ı için izlenir
                run.Events.TryWrite(new ProjectFailedEvent(run.RunId, projectId, invoke.DurationMs, reason, depIssuesForEvent));
                Decide(run.Logs, string.Format(CultureInfo.InvariantCulture,
                    "{0}: başarısız — {1} ({2}ms)", name, reason, invoke.DurationMs));
            }
        }
        catch (OperationCanceledException)
        {
            MarkStoppedFailed(run, projectId, "stopped"); // [Task-13]
            run.Events.TryWrite(new ProjectFailedEvent(run.RunId, projectId, 0, "stopped", depIssuesForEvent));
            Decide(run.Logs, $"{name}: başarısız — stopped (iptal)");
        }
        catch (Exception ex)
        {
            // Invoke/log yolunda beklenmeyen hata: proje tek başına düşer, run devam eder ("hata derlemeyi
            // öldürmez", A3) — ve aşağıdaki finally sayesinde scheduler ASLA askıda kalmaz.
            run.StoppedFailedIds.TryRemove(projectId, out _); // [Task-13] reason="invoke error: ..." — stopped DEĞİL
            run.Events.TryWrite(new ProjectFailedEvent(run.RunId, projectId, 0, "invoke error: " + ex.Message, depIssuesForEvent));
            Decide(run.Logs, $"{name}: başarısız — invoke error: {ex.Message}");
        }
        finally
        {
            run.Scheduler.Complete(projectId, result);
        }
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
    /// varsa yapılır (testlerdeki basit planner → Incremental null → persist YOK, davranış nötr). §4: yalnız
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
        { console("build-state yazılamadı (" + Path.GetFileNameWithoutExtension(projectId) + "): " + ex.Message); }
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
        BuildStateStore? StateStore,
        IncrementalPlan? Incremental);

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
