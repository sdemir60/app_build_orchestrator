using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.ProcessControl;
using BuildOrchestrator.Core.Processes;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [T4/T55] RunCoordinator: plan → N paralel worker → proje-başına invoke → disk log + IPC event → stop/continue.
/// Gerçek MSBuild YOK (Task 5/13'ün işi) — sahte <see cref="IMsBuildInvoker"/> ile deterministik: hiçbir testte
/// sleep/poll yok [D8], eşzamanlılık ve stop anları TaskCompletionSource ile kesin olarak kurulur.
/// </summary>
public class RunCoordinatorTests
{
    private static readonly TimeSpan Limit = TimeSpan.FromSeconds(30); // hang'i sonsuz bekleme değil, test hatası yapar
    private const string FakeMsBuildExe = @"C:\fake\Bin\MSBuild.exe";

    // [T20-b/P3] Gerçek MSBuild'in post-build copy çakışma satırı (MSB3021) — RetryingMsBuildInvoker'ın retry
    // kapısı ve copy-floor penceresinin TEK tetikleyicisi budur (copy'nin "başlıyor" sinyali YOKTUR).
    private const string ContentionLine =
        "A.csproj : error MSB3021: Unable to copy file \"obj\\A.dll\" to \"bin\\A.dll\". The process cannot access the file because it is being used by another process.";
    private static readonly string PlanRoot = Path.Combine(Path.GetTempPath(), "bo-coord-plan");

    private static string Id(string name) => Path.Combine(PlanRoot, name, name + ".csproj");
    private static string NameOf(string projectId) => Path.GetFileNameWithoutExtension(projectId);

    // willBuild: varsayılan null = "imza yok / pre-Sync" (mevcut çağrıların tamamı); yalnız [Task 19] Build
    // pre-skip'ini kuran testler false/true verir.
    private static ProjectNode Node(string name, string[]? deps = null, bool inCycle = false, bool? willBuild = null) =>
        new(Id(name), name, Id(name), SolutionNames: [], Dependencies: [.. (deps ?? []).Select(Id)],
            BuildOrder: 0, LayerIndex: null, LayerName: null, InCycle: inCycle, WillBuild: willBuild);

    private static RunPlan PlanOf(params ProjectNode[] nodes) => PlanOf(EmptyRefs(), nodes);

    private static RunPlan PlanOf(IReadOnlyDictionary<string, IReadOnlyList<SolutionRef>> refs, params ProjectNode[] nodes) =>
        new(new BuildPlan([.. nodes.Select((n, i) => n with { BuildOrder = i })], Cycles: [], Configuration: "Debug"), refs);

    private static Dictionary<string, IReadOnlyList<SolutionRef>> EmptyRefs() => new(StringComparer.OrdinalIgnoreCase);

    private static StartRunCommand Start(RunMode mode = RunMode.Rebuild, int parallelism = 1, string runId = "r1") =>
        new(runId, mode, PlanRoot, "Debug", parallelism);

    private static MsBuildInvokeResult Ok() => new(ExitCode: 0, DurationMs: 7, TimedOut: false, Killed: false);
    private static MsBuildInvokeResult Exit(int code) => new(code, DurationMs: 9, TimedOut: false, Killed: false);
    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static string Describe(IpcEvent e) => e switch
    {
        RunStartedEvent => "runStarted",
        BuildPreviewEvent => "buildPreview",
        PlanProgressEvent => "planProgress",
        ProjectStartedEvent p => "projectStarted:" + NameOf(p.ProjectId),
        ProjectLogEvent p => $"projectLog:{NameOf(p.ProjectId)}:{p.LineNumber}",
        ProjectSucceededEvent p => "projectSucceeded:" + NameOf(p.ProjectId),
        ProjectFailedEvent p => $"projectFailed:{NameOf(p.ProjectId)}:{p.Reason}",
        ProjectSkippedEvent p => "projectSkipped:" + NameOf(p.ProjectId),
        RunStoppedEvent s => "runStopped:" + (s.WasHard ? "hard" : "graceful"),
        RunCompletedEvent c => "runCompleted:" + c.Outcome,
        ErrorEvent er => "error:" + er.Code,
        _ => e.GetType().Name,
    };

    // ---------------------------------------------------------------- fake invoker

    private sealed class FakeInvoker(Func<MsBuildInvokeRequest, Action<string>, CancellationToken, Task<MsBuildInvokeResult>> handler)
        : IMsBuildInvoker
    {
        private readonly List<MsBuildInvokeRequest> _requests = [];
        private int _inFlight;
        private int _maxConcurrent;

        public int MaxConcurrent => Volatile.Read(ref _maxConcurrent);
        public IReadOnlyList<MsBuildInvokeRequest> Requests { get { lock (_requests) return [.. _requests]; } }

        public async Task<MsBuildInvokeResult> InvokeAsync(MsBuildInvokeRequest req, Action<string> onLine, CancellationToken ct)
        {
            lock (_requests) _requests.Add(req);
            int now = Interlocked.Increment(ref _inFlight);
            for (int max = Volatile.Read(ref _maxConcurrent); max < now; max = Volatile.Read(ref _maxConcurrent))
                if (Interlocked.CompareExchange(ref _maxConcurrent, now, max) == max) break;
            try { return await handler(req, onLine, ct); }
            finally { Interlocked.Decrement(ref _inFlight); }
        }
    }

    // ---------------------------------------------------------------- harness

    /// <summary>
    /// [Fix round 2 — YENİ 1] Belirli bir event YAZILIRKEN pump'ı duraklatan stdout. Amaç, run'ın kapanış
    /// penceresini (RunCoordinator: <c>ReleasePerf()</c> çağrıldı ama <c>_runActive=false</c> HENÜZ yazılmadı —
    /// arada <c>await pump</c> var) deterministik olarak yakalamak. Sleep/poll YOK [D8]: iki TCS randevusu.
    /// </summary>
    private sealed class PumpGateStream(string marker) : MemoryStream
    {
        private readonly TaskCompletionSource _reached = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>Marker taşıyan event yazılırken tamamlanır — o an pump duraklamıştır.</summary>
        public Task Reached => _reached.Task;

        public void Release() => _release.TrySetResult();

        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        {
            bool hit = Encoding.UTF8.GetString(buffer.Span).Contains(marker, StringComparison.Ordinal);
            await base.WriteAsync(buffer, ct);
            if (!hit) return;
            _reached.TrySetResult();
            await _release.Task;
        }
    }

    private sealed class Harness : IDisposable
    {
        private readonly MemoryStream _out;
        private readonly List<RunLogWriter> _logWriters = [];
        private long _now;

        public JobObject Job { get; } = JobObject.CreateKillOnClose();
        public string LogsRoot { get; } = Directory.CreateTempSubdirectory("bo-coord-").FullName;
        public List<string> ConsoleLines { get; } = [];

        /// <summary>[P3] Copy-contention retry'ının İSTENEN backoff süreleri. Cap-farkındalı backoff'un
        /// GERÇEK kablajını (koordinatör → <c>CoordinatorCpuFloor</c> → decorator) buradan doğrularız;
        /// decorator testlerindeki el yazması fake bu zinciri göremez.</summary>
        public List<TimeSpan> RetryDelays { get; } = [];
        public RunCoordinator Sut { get; }
        public IReadOnlyList<RunLogWriter> LogWriters { get { lock (_logWriters) return [.. _logWriters]; } }

        public Harness(RunPlan plan, FakeInvoker invoker, Func<StartRunCommand, Action<string>, RunPlan>? planner = null,
            Func<StartRunCommand, string?>? worktreeObjRootResolver = null, BuildStateStore? stateStore = null,
            ICpuGovernor? cpuGovernor = null, MemoryStream? output = null)
        {
            _out = output ?? new MemoryStream(); // [Fix round 2] testler pump'ı duraklatan bir stdout verebilir
            Sut = new RunCoordinator(
                planner: planner ?? ((_, _) => plan),
                msbuildFactory: _ => Task.FromResult(new MsBuildToolset(invoker, FakeMsBuildExe)),
                logFactory: startedAt =>
                {
                    var w = new RunLogWriter(LogsRoot, startedAt);
                    lock (_logWriters) _logWriters.Add(w);
                    return w;
                },
                writer: new NdjsonWriter(_out),
                innerJob: Job,
                nowMs: () => Volatile.Read(ref _now),
                console: line => { lock (ConsoleLines) ConsoleLines.Add(line); },
                worktreeObjRootResolver: worktreeObjRootResolver,
                stateStore: stateStore,
                cpuGovernor: cpuGovernor, // [T20-b] null ⇒ gerçek inner Job (mevcut testlerin davranışı)
                // [P3/D8] Copy-contention retry'ının backoff'u testte GERÇEK ZAMAN beklemez: üretimde
                // Task.Delay olan seam burada anında tamamlanır — istenen süre yalnız KAYDEDİLİR.
                retryDelay: (wait, _) => { lock (RetryDelays) RetryDelays.Add(wait); return Task.CompletedTask; });
        }

        /// <summary>Sahte monotonik saat — testler zamanı elle ilerletir (Thread.Sleep YOK [D8]).</summary>
        public void SetNow(long ms) => Volatile.Write(ref _now, ms);

        /// <summary>Yazılmış NDJSON satırlarını olaylara çevirir; her satır geçerli bir IpcEvent olmalı [D4].</summary>
        public IReadOnlyList<IpcEvent> Events =>
            [.. Encoding.UTF8.GetString(_out.ToArray())
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => JsonSerializer.Deserialize<IpcEvent>(l, IpcJson.Options)!)];

        public void Dispose() { Sut.Dispose(); Job.Dispose(); }
    }

    // ---------------------------------------------------------------- 1) paralellik tavanı

    [Fact]
    public async Task parallelism_ceiling_is_respected_and_workers_really_run_concurrently()
    {
        var plan = PlanOf(Node("A"), Node("B"), Node("C"), Node("D"), Node("E"), Node("F"));
        var pairInFlight = Signal();
        int arrived = 0;
        var invoker = new FakeInvoker(async (_, _, _) =>
        {
            // İlk İKİ invoke birbirini bekler: eşzamanlılık deterministik kanıtlanır (parallelism 1 olsaydı
            // bu test kilitlenir → RunCompletion.WaitAsync(Limit) ile hataya döner). Sleep/poll yok [D8].
            if (Interlocked.Increment(ref arrived) >= 2) pairInFlight.TrySetResult();
            await pairInFlight.Task;
            return Ok();
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 2), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(2, invoker.MaxConcurrent); // tavan aşılmadı (>2 yok) VE gerçekten paralel (=2)
        var events = h.Events;
        Assert.Equal(6, events.OfType<ProjectSucceededEvent>().Count());
        var done = Assert.IsType<RunCompletedEvent>(events[^1]);
        Assert.Equal(RunOutcome.Completed, done.Outcome);
        Assert.Equal(6, done.Succeeded);
        Assert.Equal(0, done.Queued);
    }

    [Fact]
    public async Task workers_park_instead_of_exiting_when_the_ready_set_is_temporarily_empty()
    {
        // [Kısıt 2] TryDispatch==false "run bitti" DEĞİL, "şu an hazır iş yok" demektir. Burada C derlenirken
        // B/D/E/F ONA bağlı olduğu için ready set BOŞTUR: naif bir `while (TryDispatch)` döngüsü, boşta kalan 3
        // worker'ı daha run'ın ilk anında ÖLDÜRÜR. Run yine de tamamlanır (son worker her Complete'ten sonra
        // yeniden bakar) — bu yüzden hata "yanlış sonuç" olarak DEĞİL, paralelliğin sessizce 1'e çökmesi olarak
        // görünür: 177 projelik bir graf tek worker'da derlenirdi. Bu testin yakaladığı budur.
        var plan = PlanOf(Node("C"), Node("B", deps: ["C"]), Node("D", deps: ["C"]), Node("E", deps: ["C"]), Node("F", deps: ["C"]));
        var leafInFlight = Signal();
        var releaseLeaf = Signal();
        var allDependentsInFlight = Signal();
        int arrived = 0;
        var invoker = new FakeInvoker(async (req, _, _) =>
        {
            if (NameOf(req.ProjectId) == "C")
            {
                leafInFlight.TrySetResult();
                await releaseLeaf.Task;   // C in-flight iken diğer 3 worker hazır iş bulamaz → park etmeli
                return Ok();
            }
            // C bitince UYANAN worker'lar burada buluşur: 4'ü de aynı anda in-flight olmalı. Park yerine
            // ölmüş worker'lar varsa buluşma gerçekleşmez ve bekleme dolar (naif döngüde MaxConcurrent=1).
            if (Interlocked.Increment(ref arrived) == 4) allDependentsInFlight.TrySetResult();
            try { await allDependentsInFlight.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
            catch (TimeoutException) { /* assert aşağıda net bir mesajla patlasın */ }
            return Ok();
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 4), default);
        await leafInFlight.Task.WaitAsync(Limit);
        releaseLeaf.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(4, invoker.MaxConcurrent); // boş ready set'ten sonra paralellik geri gelmeli
        Assert.Equal(5, h.Events.OfType<ProjectSucceededEvent>().Count());
        Assert.Equal(0, Assert.IsType<RunCompletedEvent>(h.Events[^1]).Queued);
    }

    // ---------------------------------------------------------------- 2) event sırası

    [Fact]
    public async Task event_order_is_runStarted_then_project_events_then_runCompleted()
    {
        // C ← B ← A zinciri + tek worker → deterministik dispatch sırası. B başarısız: hata dependent'i
        // BLOKLAMAZ (A3) — A yine derlenir.
        var plan = PlanOf(Node("C"), Node("B", deps: ["C"]), Node("A", deps: ["B"]));
        var invoker = new FakeInvoker((req, onLine, _) =>
        {
            onLine("derleniyor " + NameOf(req.ProjectId));
            return Task.FromResult(NameOf(req.ProjectId) == "B" ? Exit(1) : Ok());
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Proje logunun 1. satırı gerçek MSBuild komut satırı, 2. satırı build çıktısı → ikisi de projectLog.
        // İSTİSNA: A, B'ye DOĞRUDAN bağımlı ve B failed → [T54] A'nın log başına EK bir depIssue uyarı satırı
        // girer (komut satırından SONRA, gerçek çıktıdan ÖNCE) — bu yüzden A'nın logu 3 satır (C/B'ninki 2 kalır).
        Assert.Equal(
        [
            "runStarted", "buildPreview",
            "projectStarted:C", "projectLog:C:1", "projectLog:C:2", "projectSucceeded:C",
            "projectStarted:B", "projectLog:B:1", "projectLog:B:2", "projectFailed:B:exit 1",
            "projectStarted:A", "projectLog:A:1", "projectLog:A:2", "projectLog:A:3", "projectSucceeded:A",
            "runCompleted:Completed",
        ], h.Events.Select(Describe));

        var started = Assert.IsType<RunStartedEvent>(h.Events[0]);
        Assert.Equal(RunMode.Rebuild, started.Mode);
        Assert.Equal(3, started.TotalProjects);
        Assert.Equal(1, started.Parallelism);
        Assert.Equal("Debug", started.Configuration);
        Assert.Equal(0, started.ElapsedMsAtStart);

        // [T54] A, B'ye doğrudan bağımlı ve B failed → A depIssues=[B] taşır; warn satırı log'un 2. satırında.
        var succeededA = Assert.Single(h.Events.OfType<ProjectSucceededEvent>(), e => NameOf(e.ProjectId) == "A");
        Assert.Equal(["B"], succeededA.DepIssues);
        var aLog2 = Assert.Single(h.Events.OfType<ProjectLogEvent>(), e => NameOf(e.ProjectId) == "A" && e.LineNumber == 2);
        Assert.Equal("warning: B failed in this run — last successful output referenced (B)", aLog2.Text);

        // [Task 17] buildPreview, plan.Nodes'un TAMAMINI (build-order'da) taşır — henüz hiçbir proje
        // başlamadan (WillBuild burada hep null: BuildPlanBuilder run-time wiring'i henüz doldurmuyor).
        var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>());
        Assert.Equal(["C", "B", "A"], preview.Items.Select(i => NameOf(i.ProjectId)));
        Assert.All(preview.Items, i => Assert.Null(i.WillBuild));

        var done = Assert.IsType<RunCompletedEvent>(h.Events[^1]);
        Assert.Equal(2, done.Succeeded);
        Assert.Equal(1, done.Failed);
        Assert.Equal(0, done.Skipped);
        Assert.Equal(0, done.Queued);
        Assert.Equal(1, done.DepIssueCount); // yalnız A dependency-affected

        // v7Δ-7: konsol run-start özeti solution-level bir msbuild çağrısı İZLENİMİ vermez — gerçek komut
        // satırları yalnız proje loglarındadır.
        Assert.NotEmpty(h.ConsoleLines);
        Assert.DoesNotContain(h.ConsoleLines, l => l.Contains(".sln", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(h.ConsoleLines, l => l.Contains("-p:", StringComparison.Ordinal));
        Assert.DoesNotContain(h.ConsoleLines, l => l.Contains("-t:", StringComparison.Ordinal));
    }

    /// <summary>
    /// [planlama görünürlüğü] Taze bir segmentte planlama (worktree hazırlığı → tarama → graf → topo →
    /// incremental) <c>runStarted</c>'tan ÖNCE koşar ve 177 projelik bir workspace'te saniyeler sürer. O
    /// pencere eskiden TEK event bile üretmiyordu: App konsolu temizleyip <c>IsStarting</c>'e giriyor,
    /// şerit önceki metinde donuyordu — "Build'e bastım hiçbir şey olmadı".
    ///
    /// <para>Planner artık bir ilerleme kanalı alır; koordinatör onu <see cref="PlanProgressEvent"/>'e
    /// çevirir. Sıra ZORUNLUDUR: satırlar <c>runStarted</c>'tan sonra gelseydi kullanıcı zaten
    /// "▸ Building 0/N" görüyor olurdu ve satırlar geçmişe ait bir gürültüye dönerdi.</para>
    /// </summary>
    [Fact]
    public async Task planning_progress_reaches_the_app_before_runStarted()
    {
        var plan = PlanOf(Node("A"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker, planner: (_, progress) =>
        {
            progress("Scanning solutions (1)");
            progress("Computing incremental state (1 projects)");
            return plan;
        });

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var events = h.Events;
        Assert.Equal(["Scanning solutions (1)", "Computing incremental state (1 projects)"],
                     events.OfType<PlanProgressEvent>().Select(e => e.Line));

        int lastPlanAt = events.Select((e, i) => (e, i)).Last(t => t.e is PlanProgressEvent).i;
        int startedAt = events.Select((e, i) => (e, i)).First(t => t.e is RunStartedEvent).i;
        Assert.True(lastPlanAt < startedAt,
            "planlama satırları runStarted'dan ÖNCE bekleniyor; gelen sıra: " + string.Join(" | ", events.Select(Describe)));
    }

    /// <summary>Sürdürülen segment (Continue/RetryFailed) planner'ı HİÇ çağırmaz — aynı plan üstünden devam
    /// eder. Orada planlama satırı basmak, koşmayan bir işi anlatmak olurdu.</summary>
    [Fact]
    public async Task a_resumed_segment_prints_no_planning_progress()
    {
        var plan = PlanOf(Node("A"), Node("B"));
        int planned = 0;
        int aCalls = 0;
        var invoker = new FakeInvoker((req, _, _) => Task.FromResult(
            NameOf(req.ProjectId) == "A" && Interlocked.Increment(ref aCalls) == 1 ? Exit(1) : Ok()));
        using var h = new Harness(plan, invoker, planner: (_, progress) =>
        {
            Interlocked.Increment(ref planned);
            progress("Scanning solutions (1)");
            return plan;
        });

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);
        int afterFirst = h.Events.OfType<PlanProgressEvent>().Count();
        Assert.Equal(1, afterFirst); // taze segment satırını bastı (vakum değil)

        await h.Sut.StartAsync(Start(RunMode.RetryFailed, parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(1, Volatile.Read(ref planned));                            // planner yalnız taze segmentte
        Assert.Equal(afterFirst, h.Events.OfType<PlanProgressEvent>().Count()); // 2. segment satır EKLEMEDİ
    }

    // ---------------------------------------------------------------- 3) graceful stop

    [Fact]
    public async Task graceful_stop_lets_in_flight_project_finish_and_leaves_the_rest_queued()
    {
        var plan = PlanOf(Node("A"), Node("B"), Node("C"), Node("D"));
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) => { inFlight.TrySetResult(); await release.Task; return Ok(); });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await inFlight.Task.WaitAsync(Limit);           // A gerçekten in-flight (tek worker → B henüz dispatch edilmedi)
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));
        release.SetResult();                            // I2-K1: in-flight child ÖLDÜRÜLMEZ, post-build copy dahil biter
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var events = h.Events;
        Assert.Single(events.OfType<ProjectStartedEvent>());   // stop sonrası yeni dispatch YOK
        Assert.Single(events.OfType<ProjectSucceededEvent>()); // in-flight olan kendi sonucunu verdi
        Assert.Equal("runStopped:graceful", Describe(events[^2]));
        var done = Assert.IsType<RunCompletedEvent>(events[^1]);
        Assert.Equal(RunOutcome.Stopped, done.Outcome);
        Assert.Equal(1, done.Succeeded);
        Assert.Equal(3, done.Queued);                          // B, C, D — kısıt 4: snapshot in-flight tükendikten sonra
        Assert.True(h.Sut.HasResumableRun);
    }

    // [Task 18] TryGetProjectLogSnapshot artık gerçek disk okumasını (canlı writer'ın FileStream'i ya da
    // ReadProjectLogFromDisk'in File.ReadAllText'i) _gate DIŞINDA yapıyor — kilit altında yalnız "hangi
    // kaynaktan okunacağı" (canlı writer referansı / en son run dizini) yakalanır. Bu test, bir proje
    // in-flight'ken (log aktif büyürken) EŞZAMANLI bir getProjectLog + bir stopRun isteğinin BİRBİRİNİ
    // BEKLEMEDEN (aynı Task.WhenAll içinde, ikisi de ayrı thread'lerden) doğru sonuçlanabildiğini kanıtlar:
    // ne snapshot stop'u, ne stop snapshot'ı bloklar — run normal şekilde durur. (Gerçek disk I/O test
    // ortamında yeterince yavaş değildir; bu yüzden burada "kilit süresi azaldı" TIMING'i değil, YENİ
    // eşzamanlı erişim deseninin DOĞRULUĞU kanıtlanır — bkz. Task 18 raporu.)
    [Fact]
    public async Task getting_a_project_log_snapshot_concurrently_with_a_stop_request_does_not_deadlock_and_both_succeed()
    {
        var plan = PlanOf(Node("A"), Node("B"));
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (req, onLine, _) =>
        {
            if (NameOf(req.ProjectId) != "A") return Ok();
            onLine("line-1");
            inFlight.TrySetResult();
            await release.Task;
            return Ok();
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await inFlight.Task.WaitAsync(Limit); // A in-flight, logu aktif (komut satırı + "line-1" diske yazılmış)

        // İki bağımsız thread'den AYNI ANDA: biri log snapshot'ı ister, diğeri stop ister — ikisi de _gate'e
        // dokunuyor (TryGetProjectLogSnapshot artık yalnız KISA bir capture için, TryRequestStop hep KISA).
        var snapshotTask = Task.Run(() => h.Sut.TryGetProjectLogSnapshot(Id("A"), out string text, out int through)
            ? (Found: true, Text: text, Through: through) : (Found: false, Text: "", Through: 0));
        var stopTask = Task.Run(() => h.Sut.TryRequestStop(StopKind.Graceful));
        await Task.WhenAll(snapshotTask, stopTask).WaitAsync(Limit); // takılırsa Limit'te timeout ile FAIL eder

        var snap = await snapshotTask;
        Assert.True(snap.Found);
        Assert.Contains("line-1", snap.Text);
        Assert.True(snap.Through >= 1);
        Assert.True(await stopTask);

        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var done = Assert.IsType<RunCompletedEvent>(h.Events[^1]);
        Assert.Equal(RunOutcome.Stopped, done.Outcome);
        Assert.Equal(1, done.Succeeded); // A in-flight'tı, kendi sonucunu verdi
        Assert.Equal(1, done.Queued);    // B hiç dispatch edilmedi
    }

    // ---------------------------------------------------------------- 4) hard stop

    [Fact]
    public async Task hard_stop_reports_in_flight_project_as_failed_stopped_before_run_events()
    {
        var plan = PlanOf(Node("A"), Node("B"), Node("C"), Node("D"));
        var inFlight = Signal();
        var release = Signal();
        // Gerçekte TerminateJobObject in-flight MSBuild.exe'yi öldürür ve invoke sıfırdan farklı exit ile döner;
        // sahte invoker bunu exit=1 ile taklit eder. Kritik: rapor "exit 1" DEĞİL "stopped" olmalı.
        var invoker = new FakeInvoker(async (_, _, _) => { inFlight.TrySetResult(); await release.Task; return Exit(1); });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Hard));
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var events = h.Events;
        var failed = Assert.Single(events.OfType<ProjectFailedEvent>());
        Assert.Equal("stopped", failed.Reason);
        // Kısıt 4: "child öldürüldü" ≠ "sonuç raporlandı" — sonuç, run olaylarından ÖNCE yazılır.
        Assert.Equal("runStopped:hard", Describe(events[^2]));
        var done = Assert.IsType<RunCompletedEvent>(events[^1]);
        Assert.Equal(RunOutcome.Stopped, done.Outcome);
        Assert.Equal(1, done.Failed);
        Assert.Equal(3, done.Queued);
        Assert.True(h.Sut.HasResumableRun); // terminate edilmiş job yeni assign kabul eder → Continue çalışır
    }

    // ---------------------------------------------------------------- 5) continue

    [Fact]
    public async Task continue_dispatches_only_queued_projects_into_the_same_run_and_preserves_elapsed()
    {
        var plan = PlanOf(Node("A"), Node("B"), Node("C"), Node("D"));
        var inFlight = Signal();
        var release = Signal();
        Action<long>? setNow = null;
        var invoker = new FakeInvoker(async (req, _, _) =>
        {
            // Saat elle ilerletilir (gerçek zaman beklenmez [D8]): 1. segmentte 500ms, 2. segmentte +300ms geçer.
            if (NameOf(req.ProjectId) == "B") setNow!(800);
            if (NameOf(req.ProjectId) != "A") return Ok();   // yalnız 1. segmentin projesi kapıda bekler
            setNow!(500);
            inFlight.TrySetResult();
            await release.Task;
            return Ok();
        });
        using var h = new Harness(plan, invoker);
        setNow = h.SetNow;

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await inFlight.Task.WaitAsync(Limit);
        h.Sut.TryRequestStop(StopKind.Graceful);
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        int eventsAfterFirstSegment = h.Events.Count;
        Assert.True(h.Sut.HasResumableRun);

        await h.Sut.StartAsync(Start(RunMode.Continue, parallelism: 1, runId: "r1"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Yalnız queued'lar derlendi: A bir kez (1. segment), B/C/D birer kez (2. segment) — A ASLA yeniden.
        Assert.Equal(["A", "B", "C", "D"], invoker.Requests.Select(r => NameOf(r.ProjectId)));

        var second = h.Events.Skip(eventsAfterFirstSegment).ToList();
        var started = Assert.IsType<RunStartedEvent>(second[0]);
        Assert.Equal(RunMode.Continue, started.Mode);
        Assert.Equal(500, started.ElapsedMsAtStart); // T55: süre sayacı sıfırlanmaz, 1. segmentten devralınır
        Assert.Equal(4, started.TotalProjects);

        var done = Assert.IsType<RunCompletedEvent>(second[^1]);
        Assert.Equal(RunOutcome.Completed, done.Outcome);
        Assert.Equal(4, done.Succeeded); // kümülatif: A (1. segment) + B, C, D
        Assert.Equal(0, done.Queued);
        Assert.Equal(800, done.DurationMs); // 500 (devralınan) + 300 (2. segment) — segmentler TOPLANIR

        Assert.Single(h.LogWriters);       // run başına TEK RunLogWriter → Continue aynı run dizinine yazar
        Assert.False(h.Sut.HasResumableRun);
    }

    [Fact]
    public async Task stop_during_planning_still_acks_run_stopped_even_though_run_never_started()
    {
        // Kullanıcı 177 projelik bir planlama sürerken Stop'a basabilir. TryRequestStop true döndüğü an ACK BORCU
        // doğar; ama plan başarısız olursa (planFailed) run hiç başlamaz ve normal runStopped yolu çalışmaz.
        // Koordinatör bu borcu yine de kapatmalı — aksi halde App sonsuza dek runStopped bekler.
        var planningReached = Signal();
        var releasePlanning = Signal();
        Func<StartRunCommand, Action<string>, RunPlan> blockingPlanner = (_, _) =>
        {
            planningReached.TrySetResult();
            releasePlanning.Task.GetAwaiter().GetResult(); // deterministik blok (sleep-poll YOK [D8])
            throw new IOException("disk okunamadı"); // planlama başarısız → run başlamadan çıkılır
        };
        using var h = new Harness(PlanOf(Node("A")), new FakeInvoker((_, _, _) => Task.FromResult(Ok())), blockingPlanner);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await planningReached.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful)); // planlama sürerken Stop → ack borcu doğdu
        releasePlanning.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var events = h.Events;
        Assert.Contains(events, e => e is ErrorEvent { Code: "planFailed" });
        var stopped = Assert.Single(events.OfType<RunStoppedEvent>()); // ack TAM BİR KEZ
        Assert.False(stopped.WasHard);
        Assert.Empty(events.OfType<RunStartedEvent>());   // run hiç başlamadı
        Assert.Empty(events.OfType<RunCompletedEvent>()); // ...bu yüzden runCompleted da yok
    }

    [Fact]
    public async Task worktree_preparation_failure_on_a_different_branch_ends_the_run_as_planFailed_and_never_starts_it()
    {
        // [Fix wave 1 — Finding 3] Seçili branch aktif branch'ten FARKLIYSA worktree ZORUNLUDUR (K1): hazırlık
        // başarısız olursa in-place'e DÜŞÜLEMEZ — aksi halde "X'i derle" denmişken sessizce kullanıcının kirli
        // aktif branch'i derlenirdi. Program.PrepareAsync bunu WorktreePreparationException ile bildirir; burada
        // kanıtlanan, koordinatörün onu MEVCUT run-bitiren hata kanalına (planFailed — App'in
        // RunEndingErrorCodes kümesindeki kod) çevirdiği ve run'ın HİÇ başlamadığıdır.
        const string message = "Cannot build branch 'feature-x': it is not the branch checked out in the workspace, "
            + "so it must be built in an isolated worktree (the active branch is never checked out). "
            + "Worktree preparation failed: fatal: 'C:/pool/feature-x-1' already exists";
        Func<StartRunCommand, Action<string>, RunPlan> failingPlanner = (_, _) => throw new WorktreePreparationException(message);
        using var h = new Harness(PlanOf(Node("A")), new FakeInvoker((_, _, _) => Task.FromResult(Ok())), failingPlanner);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var err = Assert.Single(h.Events.OfType<ErrorEvent>());
        Assert.Equal("planFailed", err.Code); // "runFailed" (beklenmeyen iç hata) DEĞİL — kasıtlı, tanımlı red
        Assert.Equal(message, err.Message);   // kullanıcı hangi branch'in ve NEDEN derlenmediğini görür
        Assert.Empty(h.Events.OfType<RunStartedEvent>());   // run hiç başlamadı → yanlış branch DERLENMEDİ
        Assert.Empty(h.Events.OfType<RunCompletedEvent>());
        Assert.False(h.Sut.HasResumableRun);
    }

    [Fact]
    public async Task continue_without_a_resumable_run_errors()
    {
        using var h = new Harness(PlanOf(Node("A")), new FakeInvoker((_, _, _) => Task.FromResult(Ok())));

        await h.Sut.StartAsync(Start(RunMode.Continue), default);

        var err = Assert.Single(h.Events.OfType<ErrorEvent>());
        Assert.Equal("noResumableRun", err.Code);
        Assert.Empty(h.Events.OfType<RunStartedEvent>());
        Assert.Empty(h.LogWriters);
    }

    // ---------------------------------------------------------------- 5b) RetryFailed + Continue re-queue stopped (Task-13)

    [Fact]
    public async Task continue_requeues_reason_stopped_failed_projects_but_leaves_other_failure_reasons_failed()
    {
        // A: normal build hatası (exit 1) — torn DLL değil, Continue'da DOKUNULMAMALI.
        // B: hard Stop'ta mid-build yarıda kalır (reason=stopped) — torn-DLL guard: Continue'da YENİDEN derlenmeli.
        // C: hiç dispatch edilmeden Queued kalır (sıradan Continue davranışı, değişmez).
        var plan = PlanOf(Node("A"), Node("B"), Node("C"));
        var inFlight = Signal();
        var release = Signal();
        int bCalls = 0;
        var invoker = new FakeInvoker(async (req, _, _) =>
        {
            string n = NameOf(req.ProjectId);
            if (n == "A") return Exit(1);
            if (n == "B" && Interlocked.Increment(ref bCalls) == 1)
            {
                inFlight.TrySetResult();
                await release.Task; // hard Stop burada yakalar — invoke exit=1 döner ama ReasonFor "stopped" yazar
                return Exit(1);
            }
            return Ok(); // B'nin 2. çağrısı (Continue'da retry) ve C
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Hard));
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var seg1 = h.Events;
        var failed1 = seg1.OfType<ProjectFailedEvent>().ToList();
        Assert.Equal(2, failed1.Count);
        Assert.Equal("exit 1", failed1.Single(e => NameOf(e.ProjectId) == "A").Reason);
        Assert.Equal("stopped", failed1.Single(e => NameOf(e.ProjectId) == "B").Reason);
        var done1 = Assert.IsType<RunCompletedEvent>(seg1[^1]);
        Assert.Equal(RunOutcome.Stopped, done1.Outcome);
        Assert.Equal(2, done1.Failed);
        Assert.Equal(1, done1.Queued); // C hiç dispatch edilmedi

        await h.Sut.StartAsync(Start(RunMode.Continue, parallelism: 1, runId: "r1"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // A ASLA yeniden dispatch edilmez (farklı reason — Failed kalır); B torn-DLL guard ile YENİDEN derlenir;
        // C sıradan Continue davranışıyla dispatch edilir.
        Assert.Equal(["A", "B", "B", "C"], invoker.Requests.Select(r => NameOf(r.ProjectId)));

        var seg2 = h.Events.Skip(seg1.Count).ToList();
        var done2 = Assert.IsType<RunCompletedEvent>(seg2[^1]);
        Assert.Equal(RunOutcome.Completed, done2.Outcome);
        Assert.Equal(2, done2.Succeeded); // B (retried) + C
        Assert.Equal(1, done2.Failed);    // A hâlâ failed
        Assert.Equal(0, done2.Queued);

        Assert.Single(h.LogWriters); // console/stream SIFIRLANMAZ — Continue AYNI run dizinine/writer'a yazar
    }

    [Fact]
    public async Task retry_failed_rebuilds_failed_projects_and_their_transitive_dependents_only()
    {
        // F1 ← D1 ← D2 zinciri (D1 F1'e, D2 D1'e bağımlı — transitive dependent); S bağımsız.
        var plan = PlanOf(Node("F1"), Node("D1", deps: ["F1"]), Node("D2", deps: ["D1"]), Node("S"));
        int f1Calls = 0;
        var invoker = new FakeInvoker((req, _, _) =>
        {
            if (NameOf(req.ProjectId) == "F1")
                return Task.FromResult(Interlocked.Increment(ref f1Calls) == 1 ? Exit(1) : Ok());
            return Task.FromResult(Ok());
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var seg1 = h.Events;
        var done1 = Assert.IsType<RunCompletedEvent>(seg1[^1]);
        Assert.Equal(RunOutcome.Completed, done1.Outcome);
        Assert.Equal(1, done1.Failed);    // F1
        Assert.Equal(3, done1.Succeeded); // D1, D2, S ("hata derlemeyi öldürmez" — A3 — bloklanmadan build edildiler)

        await h.Sut.StartAsync(Start(RunMode.RetryFailed, parallelism: 1, runId: "r1"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Retry kümesi = F1 + transitive dependent'ları (D1, D2) — S ASLA yeniden dispatch edilmez.
        Assert.Equal(["F1", "D1", "D2", "S", "F1", "D1", "D2"], invoker.Requests.Select(r => NameOf(r.ProjectId)));

        var seg2 = h.Events.Skip(seg1.Count).ToList();
        var started2 = Assert.IsType<RunStartedEvent>(seg2[0]);
        Assert.Equal(RunMode.RetryFailed, started2.Mode);
        var done2 = Assert.IsType<RunCompletedEvent>(seg2[^1]);
        Assert.Equal(RunOutcome.Completed, done2.Outcome);
        Assert.Equal(4, done2.Succeeded); // F1 (retried, artık succeeded) + D1 + D2 + S (kümülatif)
        Assert.Equal(0, done2.Failed);
        Assert.Equal(0, done2.Queued);

        Assert.Single(h.LogWriters); // console/stream SIFIRLANMAZ — RetryFailed AYNI run dizinine/writer'a yazar
    }

    [Fact]
    public async Task retry_failed_without_a_retryable_run_errors()
    {
        using var h = new Harness(PlanOf(Node("A")), new FakeInvoker((_, _, _) => Task.FromResult(Ok())));

        await h.Sut.StartAsync(Start(RunMode.RetryFailed), default);

        var err = Assert.Single(h.Events.OfType<ErrorEvent>());
        Assert.Equal("noResumableRun", err.Code);
        Assert.Empty(h.Events.OfType<RunStartedEvent>());
        Assert.Empty(h.LogWriters);
    }

    [Fact]
    public async Task dep_issues_accumulated_in_the_first_segment_are_inherited_by_a_dependent_dispatched_after_continue()
    {
        // [T54 carry] R (kök) 1. segmentte failed olur; M (R'ye DOĞRUDAN bağımlı) aynı segmentte succeeded olur
        // ve depIssues=[R] taşıdığı _depIssuesById birikimine yazılır; run M'den SONRA (Dep dispatch edilmeden
        // önce) Stop edilir. Continue'da Dep (M'ye bağımlı, R'ye DEĞİL) dispatch edilince R artık Completed'ta
        // FAILED değildir aramaz (M zaten Succeeded) — depIssues'unu YALNIZ 1. segmentten devralınan
        // _depIssuesById["M"]=[R] üzerinden (DOLAYLI/inherited) alabilir. Bu, birikimin segment sınırını
        // AŞARAK aynı örnekle devretmesini kanıtlar.
        var plan = PlanOf(Node("R"), Node("M", deps: ["R"]), Node("Dep", deps: ["M"]));
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (req, _, _) =>
        {
            string n = NameOf(req.ProjectId);
            if (n == "R") return Exit(1);
            if (n == "M")
            {
                inFlight.TrySetResult();
                await release.Task;
                return Ok();
            }
            return Ok(); // Dep
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var seg1 = h.Events;
        var succeededM = Assert.Single(seg1.OfType<ProjectSucceededEvent>());
        Assert.Equal(["R"], succeededM.DepIssues); // sanity: M, R'ye DOĞRUDAN bağımlı
        var done1 = Assert.IsType<RunCompletedEvent>(seg1[^1]);
        Assert.Equal(RunOutcome.Stopped, done1.Outcome);
        Assert.Equal(1, done1.Queued); // Dep hiç dispatch edilmedi

        await h.Sut.StartAsync(Start(RunMode.Continue, parallelism: 1, runId: "r1"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var seg2 = h.Events.Skip(seg1.Count).ToList();
        var depEvent = Assert.Single(seg2.OfType<ProjectSucceededEvent>(), e => NameOf(e.ProjectId) == "Dep");
        Assert.Equal(["R"], depEvent.DepIssues); // 1. segmentten MİRAS — _depIssuesById segment sınırını aştı
    }

    // ---------------------------------------------------------------- 6) tek seferde tek run

    [Fact]
    public async Task second_start_run_while_one_is_running_errors_with_run_in_progress()
    {
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) => { inFlight.TrySetResult(); await release.Task; return Ok(); });
        using var h = new Harness(PlanOf(Node("A"), Node("B")), invoker);

        await h.Sut.StartAsync(Start(parallelism: 1, runId: "r1"), default);
        await inFlight.Task.WaitAsync(Limit);
        await h.Sut.StartAsync(Start(parallelism: 1, runId: "r2"), default); // A6: orchestrator tek seferde tek run
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var err = Assert.Single(h.Events.OfType<ErrorEvent>());
        Assert.Equal("runInProgress", err.Code);
        Assert.Single(h.Events.OfType<RunStartedEvent>());
        Assert.Single(h.Events.OfType<RunCompletedEvent>());
        Assert.Single(h.LogWriters);
    }

    // ---------------------------------------------------------------- 7) stdout yalnız NDJSON + log ilk satırı

    [Fact]
    public async Task hostile_build_output_stays_ndjson_and_project_log_starts_with_the_real_msbuild_command_line()
    {
        var plan = PlanOf(Node("A"));
        var invoker = new FakeInvoker((_, onLine, _) =>
        {
            onLine("gömülü\nyeni satır\rve \"tırnak\" ve \\ters bölü");   // framing'i kırmaya çalışan satır [D4]
            onLine("Türkçe: İstanbul Şükrü — ölçüm 42");
            onLine("""{"type":"pong","seq":9}""");                        // NDJSON taklidi: kaçırılmalı, olay olmamalı
            return Task.FromResult(Ok());
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var events = h.Events; // her satır IpcEvent olarak parse edilemezse burada patlar = D4 ihlali
        Assert.Empty(events.OfType<PongEvent>()); // sahte "pong" satırı escape edildi, ayrı bir olay olmadı
        var logs = events.OfType<ProjectLogEvent>().ToList();
        Assert.Equal([1, 2, 3, 4], logs.Select(l => l.LineNumber)); // komut satırı + 3 çıktı satırı, ardışık

        string expectedCommandLine = WindowsCommandLine.Build(FakeMsBuildExe,
            [.. MsBuildArguments.Build(Id("A"), "Debug", baseIntermediateOutputPath: null)]);
        Assert.Equal(expectedCommandLine, logs[0].Text);

        string[] diskLines = File.ReadAllLines(h.LogWriters[0].ProjectLogPath(Id("A")));
        Assert.Equal(4, diskLines.Length);                     // gömülü CR/LF tek satıra sanitize edildi
        Assert.Equal(expectedCommandLine, diskLines[0]);       // v7Δ-7: proje logunun İLK satırı gerçek komut satırı
        Assert.Equal(logs.Select(l => l.Text), diskLines);     // canlı akış ile disk logu birebir aynı (T28 dikişi)
    }

    // ---------------------------------------------------------------- 8) cycle pre-skip

    [Fact]
    public async Task cycle_members_are_skipped_once_and_not_re_emitted_on_continue()
    {
        var plan = PlanOf(Node("X", deps: ["Y"], inCycle: true), Node("Y", deps: ["X"], inCycle: true), Node("A"), Node("B"));
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (req, _, _) =>
        {
            if (NameOf(req.ProjectId) != "A") return Ok();   // yalnız 1. segmentin projesi kapıda bekler
            inFlight.TrySetResult();
            await release.Task;
            return Ok();
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await inFlight.Task.WaitAsync(Limit);
        h.Sut.TryRequestStop(StopKind.Graceful);
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var firstSegment = h.Events;
        Assert.Equal(["projectSkipped:X", "projectSkipped:Y"],
            firstSegment.OfType<ProjectSkippedEvent>().Select(Describe));
        Assert.Equal(["A"], invoker.Requests.Select(r => NameOf(r.ProjectId))); // cycle üyeleri asla dispatch edilmez

        await h.Sut.StartAsync(Start(RunMode.Continue), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Kısıt 7: resume edilmiş scheduler'ın PreSkipped'i boştur — X/Y için TEKRAR projectSkipped yazılmaz.
        var second = h.Events.Skip(firstSegment.Count).ToList();
        Assert.Empty(second.OfType<ProjectSkippedEvent>());
        var done = Assert.IsType<RunCompletedEvent>(second[^1]);
        Assert.Equal(2, done.Skipped);   // X, Y (kümülatif)
        Assert.Equal(2, done.Succeeded); // A, B
        Assert.Equal(0, done.Queued);
    }

    // ---------------------------------------------------------------- 9) gerçek Supervisor process'i (wiring)

    [SkippableFact] // Program.cs + SupervisorHost wiring: startRun GERÇEKTEN koordinatöre bağlı mı, stdout YALNIZ
                    // NDJSON mu [D4] — in-process testler bu iki dosyayı hiç çalıştırmaz (ör. host ile koordinatör
                    // için AYRI NdjsonWriter kurmak satırları iç içe geçirirdi, ancak burada yakalanır).
    public async Task real_supervisor_process_wires_start_run_and_keeps_stdout_ndjson_only()
    {
        string logsDir = Directory.CreateTempSubdirectory("bo-sup-logs-").FullName;
        string root = Directory.CreateTempSubdirectory("bo-sup-ws-").FullName;
        // X ↔ Y: HintPath ile birbirine bağlı iki csproj → TopoSort ikisini de SCC üyesi işaretler → ikisi de
        // pre-skip edilir. Böylece GERÇEK bir run akışı gözlenir ama hiçbir MSBuild child'ı doğmaz (Task 5/13'ün işi).
        foreach (var (self, other) in new[] { ("X", "Y"), ("Y", "X") })
        {
            Directory.CreateDirectory(Path.Combine(root, self));
            await File.WriteAllTextAsync(Path.Combine(root, self, self + ".csproj"),
                $"""
                <Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup><AssemblyName>OSYS.{self}</AssemblyName></PropertyGroup>
                  <ItemGroup><Reference Include="OSYS.{other}"><HintPath>..\{other}\bin\OSYS.{other}.dll</HintPath></Reference></ItemGroup>
                </Project>
                """);
        }

        using var p = Process.Start(TestPaths.Psi(logsDir))!;
        var ipcWriter = new NdjsonWriter(p.StandardInput.BaseStream);
        var ipcReader = new NdjsonReader(p.StandardOutput.BaseStream);
        Assert.IsType<EngineReadyEvent>(await ipcReader.ReadAsync<IpcEvent>().WaitAsync(Limit));

        await ipcWriter.WriteAsync(new StartRunCommand("r1", RunMode.Rebuild, root, "Debug", 2));
        var received = new List<IpcEvent>();
        while (true)
        {
            var e = await ipcReader.ReadAsync<IpcEvent>().WaitAsync(Limit) // parse edilemeyen satır = D4 ihlali = FAIL
                    ?? throw new InvalidOperationException("Supervisor stdout beklenmedik şekilde kapandı.");
            // vswhere/VS kurulu değilse run başlamadan msbuildNotFound gelir — mevcut MsBuildInvokerTests deseni.
            if (e is ErrorEvent { Code: "msbuildNotFound" } err) Skip.If(true, err.Message);
            received.Add(e);
            if (e is RunCompletedEvent) break;
        }

        // [planlama görünürlüğü] Eski iddia: akış <c>runStarted</c> ile BAŞLARDI. Değişme gerekçesi: taze bir
        // segmentte planlama (tarama → graf → topo → incremental) runStarted'tan ÖNCE koşar ve 177 projelik
        // gerçek bir workspace'te saniyeler sürer; o pencerede tek event bile üretilmediği için App konsolu
        // boş, şerit önceki metinde donuyordu. Sıra artık planlama satırlarıyla BAŞLAR. Bu test o kablajı
        // GERÇEK Supervisor process'i üzerinden görür — in-process harness'ta planner sahtedir.
        Assert.Equal(["planProgress", "planProgress", "planProgress", "planProgress", "planProgress",
                      "runStarted", "buildPreview", "projectSkipped:X", "projectSkipped:Y", "runCompleted:Completed"],
            received.Select(Describe));
        // Satırlar PAYLAŞILAN kaynaktan (PlanProgressLines — Sync ile aynı) ve GERÇEK sayılarla gelir:
        // workspace'te 0 .sln, 2 .csproj var ve X↔Y tek bir SCC oluşturur. Worktree satırı YOK: komut
        // UseWorktree=false + Branch="" taşır, yani hazırlık in-place erken-dönüşüyle hiç koşmaz.
        Assert.Equal(
        [
            PlanProgressLines.ScanningSolutions(0),
            PlanProgressLines.ReadingProjectItems(2),
            PlanProgressLines.DependencyGraph(1),
            PlanProgressLines.BuildOrderResolved(2),
            PlanProgressLines.ComputingIncremental(2),
        ], received.OfType<PlanProgressEvent>().Select(e => e.Line));
        var done = Assert.IsType<RunCompletedEvent>(received[^1]);
        Assert.Equal(2, done.Skipped);
        Assert.Equal(0, done.Queued);

        await ipcWriter.WriteAsync(new ShutdownCommand());
        string rest = await p.StandardOutput.ReadToEndAsync().WaitAsync(Limit);
        foreach (var line in rest.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            Assert.NotNull(JsonSerializer.Deserialize<IpcEvent>(line, IpcJson.Options)); // artık satır kalmamalı, kalırsa NDJSON olmalı
        await p.WaitForExitAsync(new CancellationTokenSource(5000).Token);
        Assert.Equal(0, p.ExitCode);
    }

    // ---------------------------------------------------------------- 10) invoke isteğinin şekli

    [Fact]
    public async Task invoke_request_carries_packages_config_restore_solution_dir_and_no_obj_isolation()
    {
        string root = Directory.CreateTempSubdirectory("bo-coord-ws-").FullName;
        Directory.CreateDirectory(Path.Combine(root, "A"));
        Directory.CreateDirectory(Path.Combine(root, "B"));
        string aId = Path.Combine(root, "A", "A.csproj");
        string bId = Path.Combine(root, "B", "B.csproj");
        await File.WriteAllTextAsync(Path.Combine(root, "A", "packages.config"), "<packages/>"); // A legacy restore ister
        string slnPath = Path.Combine(root, "Osys.sln");

        var refs = EmptyRefs();
        refs[aId] = [new SolutionRef("Osys", slnPath)];
        refs[bId] = [];
        var nodes = new[]
        {
            new ProjectNode(aId, "A", aId, [], [], 0, null, null, false, null),
            new ProjectNode(bId, "B", bId, [], [], 1, null, null, false, null),
        };
        var plan = new RunPlan(new BuildPlan(nodes, Cycles: [], Configuration: "Release"), refs);
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Rebuild, root, "Release", 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var a = Assert.Single(invoker.Requests, r => r.ProjectId == aId);
        Assert.True(a.NeedsRestore);                                  // packages.config yanında
        Assert.Equal(root, a.SolutionDir);                            // SolutionDirResolver: sln'in dizini
        Assert.Equal("Release", a.Configuration);
        Assert.Null(a.BaseIntermediateOutputPath);                    // I2-K2: It-2'de obj izolasyonu YOK

        var b = Assert.Single(invoker.Requests, r => r.ProjectId == bId);
        Assert.False(b.NeedsRestore);
        Assert.Equal(Path.Combine(root, "B"), b.SolutionDir);         // sln yok → projenin kendi dizini
        Assert.Null(b.BaseIntermediateOutputPath);
    }

    // ---------------------------------------------------------------- 11) worktree obj-izolasyonu (Task 10 / I2-K2)

    [Fact]
    public async Task worktree_run_with_a_supplied_resolver_gets_per_project_isolated_obj_paths()
    {
        // [I2-K2/Task 10] worktree'nin GERÇEK hazırlanması (WorktreeManager.PrepareWorktreeAsync) üretimde
        // Program.cs'in planner'ında yapılır (A4) — burada koordinatörün kendi sözleşmesi izole test edilir:
        // "worktree kökü biliniyorsa proje başına izole obj".
        string worktreeRoot = Path.Combine(Path.GetTempPath(), "bo-coord-wt-obj-" + Guid.NewGuid());
        var plan = PlanOf(Node("A"), Node("B"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker, worktreeObjRootResolver: cmd => cmd.UseWorktree ? worktreeRoot : null);

        await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Rebuild, PlanRoot, "Debug", 1, UseWorktree: true), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var a = Assert.Single(invoker.Requests, r => NameOf(r.ProjectId) == "A");
        var b = Assert.Single(invoker.Requests, r => NameOf(r.ProjectId) == "B");
        Assert.NotNull(a.BaseIntermediateOutputPath);
        Assert.NotNull(b.BaseIntermediateOutputPath);
        Assert.NotEqual(a.BaseIntermediateOutputPath, b.BaseIntermediateOutputPath); // farklı proje → farklı izole path
        Assert.Equal(WorktreeObjPathResolver.Resolve(worktreeRoot, a.ProjectId), a.BaseIntermediateOutputPath); // deterministik şema
        Assert.StartsWith(worktreeRoot, a.BaseIntermediateOutputPath!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task worktree_run_without_a_resolver_still_passes_null_obj_path() // resolver opsiyoneldir: yoksa in-place obj
    {
        var plan = PlanOf(Node("A"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker); // worktreeObjRootResolver verilmedi

        await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Rebuild, PlanRoot, "Debug", 1, UseWorktree: true), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var a = Assert.Single(invoker.Requests);
        Assert.Null(a.BaseIntermediateOutputPath); // resolver yoksa UseWorktree=true bile null'a düşer
    }

    [Fact]
    public async Task in_place_run_ignores_a_supplied_resolver_and_stays_null() // UseWorktree=false her zaman kazanır
    {
        var plan = PlanOf(Node("A"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker,
            worktreeObjRootResolver: _ => @"c:\should-not-be-used"); // UseWorktree=false olduğu için hiç ÇAĞRILMAMALI

        await h.Sut.StartAsync(Start(parallelism: 1), default); // UseWorktree varsayılan false
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var a = Assert.Single(invoker.Requests);
        Assert.Null(a.BaseIntermediateOutputPath);
    }

    [Fact]
    public async Task Continue_inherits_the_original_runs_worktree_obj_root()
    {
        // [A4] Worktree, run'ın ÇÖZÜLMÜŞ workspace'idir — segment başına yeniden hesaplanan bir istek bayrağı
        // DEĞİL. App, Continue'yu bugün UseWorktree=false ile yollar (RunViewModel.cs:241); segment-2 kökü
        // cmd'den yeniden hesaplasaydı AYNI run'ın yarısı worktree'nin izole obj'sine, yarısı projenin default
        // obj'sine derlenirdi (yarısı bayat-obj zehrine açık, üstelik sessizce).
        string worktreeRoot = Path.Combine(Path.GetTempPath(), "bo-coord-wt-continue-" + Guid.NewGuid().ToString("N"));
        var plan = PlanOf(Node("A"), Node("B"), Node("C"), Node("D"));
        var resolverCalls = new List<StartRunCommand>();
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (req, _, _) =>
        {
            if (NameOf(req.ProjectId) != "A") return Ok();
            inFlight.TrySetResult();
            await release.Task; // 1. segment A'da duruyorken Stop → B/C/D Queued kalır
            return Ok();
        });
        using var h = new Harness(plan, invoker, worktreeObjRootResolver: cmd =>
        {
            lock (resolverCalls) resolverCalls.Add(cmd);
            return worktreeRoot;
        });

        await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Rebuild, PlanRoot, "Debug", 1, UseWorktree: true), default);
        await inFlight.Task.WaitAsync(Limit);
        h.Sut.TryRequestStop(StopKind.Graceful);
        release.SetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Continue: UseWorktree BAYRAĞI YOK (App'in bugünkü davranışı) — yine de 1. segmentin kökü kullanılmalı.
        await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Continue, PlanRoot, "Debug", 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A", "B", "C", "D"], invoker.Requests.Select(r => NameOf(r.ProjectId)));
        Assert.All(invoker.Requests,
            r => Assert.StartsWith(worktreeRoot, r.BaseIntermediateOutputPath!, StringComparison.Ordinal));
        var call = Assert.Single(resolverCalls); // resolver YALNIZ taze run'da çağrılır — Continue MİRAS ALIR
        Assert.True(call.UseWorktree);
        Assert.Equal(RunMode.Rebuild, call.Mode);
    }

    // ---------------------------------------------------------------- 12) depIssue propagation (T54)

    [Fact]
    public async Task a_failed_root_s_dependents_carry_dep_issues_direct_and_inherited_and_are_not_blocked()
    {
        // C ← B ← A zinciri (B, C'ye; A, B'ye bağımlı), tek worker → deterministik sıra. C (kök) başarısız olur;
        // "hata derlemeyi öldürmez" (A3): B ve C'nin dependent'ı A yine de BLOKLANMADAN derlenir (resolved =
        // succeeded|failed|skipped) — ama B DOĞRUDAN, A ise B üzerinden MİRAS (dolaylı) olarak C'yi depIssue taşır.
        var plan = PlanOf(Node("C"), Node("B", deps: ["C"]), Node("A", deps: ["B"]));
        var invoker = new FakeInvoker((req, _, _) =>
            Task.FromResult(NameOf(req.ProjectId) == "C" ? Exit(1) : Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var events = h.Events;
        var failedC = Assert.Single(events.OfType<ProjectFailedEvent>());
        Assert.Equal("C", NameOf(failedC.ProjectId));
        Assert.Null(failedC.DepIssues); // C kendisi kök — kendi başına bir depIssue taşımaz (null, JSON'a yazılmaz)

        var succeededB = events.OfType<ProjectSucceededEvent>().Single(e => NameOf(e.ProjectId) == "B");
        Assert.Equal(["C"], succeededB.DepIssues); // B, C'ye DOĞRUDAN bağımlı ve C failed

        var succeededA = events.OfType<ProjectSucceededEvent>().Single(e => NameOf(e.ProjectId) == "A");
        Assert.Equal(["C"], succeededA.DepIssues); // A, C'ye doğrudan bağımlı DEĞİL — B üzerinden MİRAS aldı

        var done = Assert.IsType<RunCompletedEvent>(events[^1]);
        Assert.Equal(RunOutcome.Completed, done.Outcome);
        Assert.Equal(2, done.Succeeded); // A, B
        Assert.Equal(1, done.Failed);    // C
        Assert.Equal(2, done.DepIssueCount); // B ve A dependency-affected (C kendi depIssue'u boş, sayılmaz)
    }

    [Fact]
    public async Task dep_issue_warn_lines_appear_at_the_affected_project_s_log_head_direct_then_indirect_wording()
    {
        var plan = PlanOf(Node("C"), Node("B", deps: ["C"]), Node("A", deps: ["B"]));
        var invoker = new FakeInvoker((req, onLine, _) =>
        {
            onLine("gerçek derleme çıktısı " + NameOf(req.ProjectId));
            return Task.FromResult(NameOf(req.ProjectId) == "C" ? Exit(1) : Ok());
        });
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        List<string> LogTextsFor(string name) => h.Events.OfType<ProjectLogEvent>()
            .Where(e => NameOf(e.ProjectId) == name).OrderBy(e => e.LineNumber).Select(e => e.Text).ToList();

        // B: satır 1 gerçek MSBuild komut satırı (v7Δ-7 invaryantı korunur) → satır 2 DOĞRUDAN uyarı → satır 3 gerçek çıktı.
        var b = LogTextsFor("B");
        Assert.Equal("warning: C failed in this run — last successful output referenced (C)", b[1]);
        Assert.Equal("gerçek derleme çıktısı B", b[2]);

        // A: C'ye doğrudan bağımlı değil (B'ye bağımlı) → DOLAYLI (zincir) uyarısı.
        var a = LogTextsFor("A");
        Assert.Equal("warning: failure in dependency chain (C) — referenced outputs may be stale", a[1]);
        Assert.Equal("gerçek derleme çıktısı A", a[2]);

        // C kendisi kök — kendi logunda hiç depIssue uyarı satırı YOK.
        Assert.DoesNotContain(LogTextsFor("C"), l => l.StartsWith("warning:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task a_skipped_dependency_produces_no_dep_issue_for_its_dependent()
    {
        // X cycle nedeniyle construction'da Skipped (pre-skip) sayılır. Y, X'e bağımlı: X resolved sayılır
        // (bloklamaz) ama SKIPPED bir bağımlılık depIssue ÜRETMEZ (yalnız FAILED kökler taşınır — v7 A6).
        var plan = PlanOf(Node("X", deps: ["Y"], inCycle: true), Node("Y", deps: ["X"], inCycle: true), Node("Z", deps: ["X"]));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var succeededZ = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
        Assert.Equal("Z", NameOf(succeededZ.ProjectId));
        Assert.Null(succeededZ.DepIssues);

        var done = Assert.IsType<RunCompletedEvent>(h.Events[^1]);
        Assert.Equal(0, done.DepIssueCount);
    }

    // ---------------------------------------------------------------- 13) StaleObjDetector wiring (Task 14/T72)

    // OSYS.Types.NewSales.Print vakasının aynısı: v4.6 legacy csproj + obj altında yabancı (netstandard2.0)
    // restore artığı — Reason metnindeki "foreign TFM" ifadesi test boyunca stale-warn işareti olarak kullanılır.
    // [A13/B2] metin İngilizceye çevrildi (uygulama İngilizce-only) — işaretin kendisi aynı ayırt ediciliği taşır.
    private const string StaleMarker = "foreign TFM";

    private static string WriteStaleObjProject(string dir, string assemblyName)
    {
        Directory.CreateDirectory(Path.Combine(dir, "obj"));
        string proj = Path.Combine(dir, assemblyName + ".csproj");
        File.WriteAllText(proj, $"""
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <AssemblyName>{assemblyName}</AssemblyName>
                <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"),
            "{ \"targets\": { \".NETStandard,Version=v2.0\": {} } }"); // yabancı
        return proj;
    }

    [Fact]
    public async Task fresh_in_place_run_warns_once_on_stale_obj_via_console_and_decision_log_and_never_touches_the_obj()
    {
        string root = Path.Combine(Path.GetTempPath(), "bo-coord-staleobj-" + Guid.NewGuid().ToString("N"));
        try
        {
            string proj = WriteStaleObjProject(Path.Combine(root, "P"), "P");
            string assets = Path.Combine(root, "P", "obj", "project.assets.json");
            byte[] before = File.ReadAllBytes(assets);

            var node = new ProjectNode(proj, "P", proj, [], [], 0, null, null, false, null);
            var plan = PlanOf(node);
            var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
            using var h = new Harness(plan, invoker); // worktreeObjRootResolver YOK → in-place

            await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Rebuild, root, "Debug", 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            var warnLines = h.ConsoleLines.Where(l => l.Contains(StaleMarker, StringComparison.Ordinal)).ToList();
            var warn = Assert.Single(warnLines);
            Assert.Contains("P", warn);

            string decisionLog = File.ReadAllText(Path.Combine(h.LogWriters.Single().RunDirectory, "decision.log"));
            Assert.Contains(StaleMarker, decisionLog); // aynı satır decision.log'a da yazılır (onRetry ile aynı ikili-yazım deseni)

            Assert.Equal(before, File.ReadAllBytes(assets)); // [§4] dokunulmadı — byte-tam aynı
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task does_not_warn_on_a_clean_project()
    {
        string root = Path.Combine(Path.GetTempPath(), "bo-coord-staleobj-" + Guid.NewGuid().ToString("N"));
        try
        {
            string dir = Path.Combine(root, "P");
            Directory.CreateDirectory(Path.Combine(dir, "obj"));
            string proj = Path.Combine(dir, "P.csproj");
            File.WriteAllText(proj, """
                <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
                  <PropertyGroup>
                    <AssemblyName>P</AssemblyName>
                    <TargetFrameworkVersion>v4.6</TargetFrameworkVersion>
                  </PropertyGroup>
                </Project>
                """);
            File.WriteAllText(Path.Combine(dir, "obj", "project.assets.json"),
                "{ \"targets\": { \".NETFramework,Version=v4.6\": {} } }"); // temiz

            var node = new ProjectNode(proj, "P", proj, [], [], 0, null, null, false, null);
            var plan = PlanOf(node);
            var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
            using var h = new Harness(plan, invoker);

            await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Rebuild, root, "Debug", 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.DoesNotContain(h.ConsoleLines, l => l.Contains(StaleMarker, StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task worktree_run_does_not_warn_even_when_the_project_s_default_obj_is_stale()
    {
        // [I2-K2/Task 10] worktree run izole obj kullanır (BaseIntermediateOutputPath worktree altına yönlenir) —
        // projenin KENDİ (default) obj'i hiç okunmaz/derlenmez, bu yüzden bayat-obj teşhisi anlamsızdır.
        string root = Path.Combine(Path.GetTempPath(), "bo-coord-staleobj-" + Guid.NewGuid().ToString("N"));
        string worktreeRoot = Path.Combine(Path.GetTempPath(), "bo-coord-staleobj-wt-" + Guid.NewGuid().ToString("N"));
        try
        {
            string proj = WriteStaleObjProject(Path.Combine(root, "P"), "P");

            var node = new ProjectNode(proj, "P", proj, [], [], 0, null, null, false, null);
            var plan = PlanOf(node);
            var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
            using var h = new Harness(plan, invoker, worktreeObjRootResolver: cmd => cmd.UseWorktree ? worktreeRoot : null);

            await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Rebuild, root, "Debug", 1, UseWorktree: true), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.DoesNotContain(h.ConsoleLines, l => l.Contains(StaleMarker, StringComparison.Ordinal));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task continue_segment_does_not_re_emit_the_stale_obj_warning_from_the_fresh_segment()
    {
        string root = Path.Combine(Path.GetTempPath(), "bo-coord-staleobj-" + Guid.NewGuid().ToString("N"));
        try
        {
            string proj = WriteStaleObjProject(Path.Combine(root, "P"), "P");
            var pNode = new ProjectNode(proj, "P", proj, [], [], 0, null, null, false, null);
            // Q/R gerçek diskte YOK (nonexistent fake path) — StaleObjRunStartWarner bunları ASLA fırlatmadan
            // sessizce atlamalı (never-throw); yalnız Q'yu in-flight'ta durdurup 2. segment (Continue) tetiklenir.
            var qNode = Node("Q");
            var rNode = Node("R");
            var plan = PlanOf(pNode, qNode, rNode);

            var qInFlight = Signal();
            var releaseQ = Signal();
            var invoker = new FakeInvoker(async (req, _, _) =>
            {
                if (NameOf(req.ProjectId) == "Q") { qInFlight.TrySetResult(); await releaseQ.Task; }
                return Ok();
            });
            using var h = new Harness(plan, invoker);

            await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Rebuild, root, "Debug", 1), default);
            await qInFlight.Task.WaitAsync(Limit);
            h.Sut.TryRequestStop(StopKind.Graceful);
            releaseQ.SetResult();
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.True(h.Sut.HasResumableRun); // R hiç dispatch edilmedi → Continue'ya açık

            int warnsAfterSegment1 = h.ConsoleLines.Count(l => l.Contains(StaleMarker, StringComparison.Ordinal));
            Assert.Equal(1, warnsAfterSegment1); // 1. (fresh) segment TEK BİR KEZ warn eder

            await h.Sut.StartAsync(new StartRunCommand("r1", RunMode.Continue, root, "Debug", 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            int warnsAfterSegment2 = h.ConsoleLines.Count(l => l.Contains(StaleMarker, StringComparison.Ordinal));
            Assert.Equal(1, warnsAfterSegment2); // Continue AYNI obj üstünde devam eder — yeniden teşhis/warn YOK
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    // ---------------------------------------------------------------- 14) katman uyarıları (A1/T15)

    // Ters-katman uyarısı warn-only DATA'dır: koordinatör onu OKUYUP bloklama/yeniden sıralama YAPMAZ, yalnız
    // run başında konsola basar — tasarımın tek gerçek düzeltmesi kullanıcının pattern'leri gözden geçirmesidir.
    [Fact]
    public async Task layer_warnings_carried_by_the_plan_are_printed_to_the_console_at_run_start()
    {
        const string Warning =
            "reverse layer dependency: 'OSYS.Data' (layer 0 'DataLayer') depends on producer 'B.csproj' (layer 1 'UiLayer')";
        var plan = new RunPlan(
            new BuildPlan([Node("A") with { BuildOrder = 0 }], Cycles: [], Configuration: "Debug",
                LayerWarnings: [Warning]),
            EmptyRefs());
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Contains(h.ConsoleLines, l => l == "warning: " + Warning);
    }

    // ---------------------------------------------------------------- 15) depIssue-persist penceresi (A2)

    private static string NewCacheRoot() =>
        Path.Combine(Path.GetTempPath(), "bo-coord-state-" + Guid.NewGuid().ToString("N"));

    private static IncrementalPlan Incremental(params string[] names) =>
        new(names.ToDictionary(Id, _ => "sig", StringComparer.OrdinalIgnoreCase), "headsha", "main");

    [Fact]
    public async Task A_success_carrying_a_dep_issue_does_not_persist_build_state()
    {
        // [A2] Up fail eder, Down (Up'a bağımlı) BAŞARILI olur → Down depIssue taşır, yani Up'ın BAYAT (önceki)
        // çıktısına link'lidir. Böyle bir success için taze imza persist edilirse, Up kaynak DEĞİŞMEDEN
        // düzeldiğinde (zehirli obj temizliği sınıfı) Down'ın imzası da değişmez → sonraki Build onu "güncel"
        // sayıp pre-skip eder ve Down sonsuza dek bayat binary'e link'li kalır. Solo kontrol grubudur:
        // depIssue TAŞIMAYAN bir success persist edilmeye devam etmeli (aksi halde test önemsizce geçerdi).
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var plan = new RunPlan(
                new BuildPlan([Node("Up") with { BuildOrder = 0 }, Node("Down", deps: ["Up"]) with { BuildOrder = 1 },
                               Node("Solo") with { BuildOrder = 2 }],
                    Cycles: [], Configuration: "Debug"),
                EmptyRefs(),
                Incremental: Incremental("Up", "Down", "Solo"));
            var invoker = new FakeInvoker((req, _, _) =>
                Task.FromResult(NameOf(req.ProjectId) == "Up" ? Exit(1) : Ok()));
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            var down = Assert.Single(h.Events.OfType<ProjectSucceededEvent>(), e => NameOf(e.ProjectId) == "Down");
            Assert.Equal(["Up"], down.DepIssues); // sanity: Down gerçekten depIssue taşıyan bir success

            Assert.DoesNotContain(store.Load().Values, s => s.ProjectId == Id("Down"));
            Assert.Contains(store.Load().Values, s => s.ProjectId == Id("Solo")); // temiz success persist EDİLİR
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task The_run_start_preview_carries_each_projects_last_built_commit_from_the_state_store()
    {
        // [W1] Sync yolu BuiltCommit'i doldurup run yolu boş bıraksaydı, Build'e basar basmaz her kartın sha
        // slotunun sol yarısı sıfırlanırdı (buildPreview her run/segment başında YENİDEN yayınlanır ve satırı
        // uzlaştırır). Kayıtlı proje değeri taşır, kaydı olmayan null kalır.
        string cacheRoot = NewCacheRoot();
        try
        {
            const string builtCommit = "b7e91d4c0affee1122334455667788990aabbcc";
            var store = new BuildStateStore(cacheRoot);
            store.Upsert(new BuildState(Id("Known"), "sig", builtCommit, BuildResult.Succeeded));

            var plan = PlanOf(Node("Known"), Node("Fresh"));
            var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>());
            Assert.Equal(builtCommit, Assert.Single(preview.Items, i => NameOf(i.ProjectId) == "Known").BuiltCommit);
            Assert.Null(Assert.Single(preview.Items, i => NameOf(i.ProjectId) == "Fresh").BuiltCommit);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task A_run_without_a_state_store_still_emits_a_preview_with_no_built_commits()
    {
        // [W1] stateStore null (harness/pre-Task-19 kablajı) ⇒ Load HİÇ çağrılmaz ve preview yine yayınlanır —
        // sha alanı yalnız boş kalır. Mevcut testlerin tamamı bu yoldan geçtiği için kapı ayrıca pinlenir.
        var plan = PlanOf(Node("A"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.All(Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items, i => Assert.Null(i.BuiltCommit));
    }

    [Fact]
    public async Task Build_mode_pre_skips_up_to_date_nodes_without_invoking_msbuild_and_persists_the_built_ones()
    {
        // [Task 19/A2] RunMode.Build pre-skip yolunun ilk DETERMİNİSTİK (acceptance dışı, in-process) kanıtı:
        // WillBuild==false olan düğüm için MSBuild HİÇ çağrılmaz (yalnız "skipped — up to date" olayı yeterli
        // kanıt değildir — WillBuild'i yok sayıp yine de derleyen bir koordinatör de o olayı üretebilirdi),
        // WillBuild==true olan ise derlenir ve BuildState'i persist edilir.
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var plan = new RunPlan(
                new BuildPlan([Node("Clean", willBuild: false) with { BuildOrder = 0 },
                               Node("Dirty", willBuild: true) with { BuildOrder = 1 }],
                    Cycles: [], Configuration: "Debug"),
                EmptyRefs(),
                // [A2 fix-3] Clean'e de imza verilir: aksi halde aşağıdaki "Clean persist EDİLMEDİ" iddiası
                // önemsizce doğrudur (imzasız proje için PersistBuildStateOnSuccess zaten erken döner).
                Incremental: Incremental("Clean", "Dirty"));
            var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Start(RunMode.Build, parallelism: 1), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            var skipped = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
            Assert.Equal(Id("Clean"), skipped.ProjectId);
            Assert.Equal("skipped — up to date", skipped.Reason);
            Assert.DoesNotContain(invoker.Requests, r => r.ProjectId == Id("Clean")); // MSBuild ÇAĞRILMADI
            Assert.Equal([Id("Dirty")], invoker.Requests.Select(r => r.ProjectId));   // yalnız dirty derlendi
            Assert.Contains(store.Load().Values, s => s.ProjectId == Id("Dirty"));
            // [A2 fix-1] Clean'in de imzası VAR (Incremental("Clean", "Dirty")) — bu yüzden bu iddia artık
            // önemsizce doğru değil: pre-skip bozulup Clean derlenseydi PersistBuildStateOnSuccess erken
            // dönmez, kaydı yazardı. Yani bu satır "Clean derlendi mi" için İKİNCİ bağımsız dedektördür.
            Assert.DoesNotContain(store.Load().Values, s => s.ProjectId == Id("Clean")); // derlenmedi → persist yok
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    [Fact]
    public async Task A_failed_project_is_invalidated_in_build_state_so_the_next_Build_cannot_pre_skip_it()
    {
        // [A2 fix-1] KAYNAK DEĞİŞMEDEN başarısızlık sınıfı (zehirli obj: silinmiş bir kardeş csproj'un
        // project.assets.json/*.nuget.g.props artığı): Up dün YEŞİLDİ (Succeeded + imza persist edildi), bugün
        // AYNI imzayla FAIL ediyor. Başarısızlık build-state'e yazılmazsa kayıt hâlâ "Succeeded + eşleşen imza"
        // der ve bir sonraki Build projeyi "skipped — up to date" diye PRE-SKIP eder — kullanıcıya bozuk bir
        // proje "güncel" olarak raporlanır. §4 gereği DLL/bin timestamp'i okunmadığı için bunu yakalayabilecek
        // başka mekanizma YOKTUR. Planner, üretimdeki seam'in (Program.ComputeIncremental → IncrementalRunBinder
        // → BuildPreview/WillBuildEvaluator) aynısını kullanır: WillBuild HER run'da GÜNCEL store'dan hesaplanır.
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var inc = Incremental("Up", "Solo"); // imzalar SABİT — kaynak hiçbir run'da değişmiyor
            var basePlan = new BuildPlan([Node("Up") with { BuildOrder = 0 }, Node("Solo") with { BuildOrder = 1 }],
                Cycles: [], Configuration: "Debug");
            RunPlan Planner(StartRunCommand _, Action<string> __)
            {
                var state = store.Load();
                return new RunPlan(
                    BuildPreview.ComputeWillBuild(basePlan, n => inc.SignatureById[n.Id], state.GetValueOrDefault),
                    EmptyRefs(), Incremental: inc);
            }

            bool upFails = false;
            var invoker = new FakeInvoker((req, _, _) =>
                Task.FromResult(upFails && NameOf(req.ProjectId) == "Up" ? Exit(1) : Ok()));
            // planner verildiği için sabit plan argümanı KULLANILMAZ (Harness: planner ?? (_ => plan)).
            using var h = new Harness(PlanOf(), invoker, planner: Planner, stateStore: store);

            // ---- Run 1 ("dün"): state YOK → ikisi de derlenir, ikisi de Succeeded + imza persist eder.
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r1"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);
            Assert.Equal(BuildResult.Succeeded, store.Load()[Id("Up")].LastResult);

            // ---- Run 2 ("bugün"): Rebuild (pre-skip yok) → Up KAYNAK DEĞİŞMEDEN fail eder.
            upFails = true;
            await h.Sut.StartAsync(Start(RunMode.Rebuild, runId: "r2"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            var upAfterFailure = store.Load()[Id("Up")];
            Assert.Equal(BuildResult.Failed, upAfterFailure.LastResult);   // artık "bilinen iyi" DEĞİL
            Assert.Equal("sig", upAfterFailure.BuiltSignature);            // son BAŞARILI imza KORUNUR (Fast frozen-upstream)
            Assert.Equal(7, upAfterFailure.LastDurationMs);                // ETA: iyi süre (Ok=7ms) fail süresiyle (9ms) EZİLMEZ

            // ---- Run 3: incremental Build → Up PRE-SKIP EDİLEMEZ, GERÇEK bir MSBuild invoke'u olmalı.
            upFails = false;
            int before = invoker.Requests.Count;
            await h.Sut.StartAsync(Start(RunMode.Build, runId: "r3"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            var run3 = invoker.Requests.Skip(before).Select(r => r.ProjectId).ToList();
            Assert.Equal([Id("Up")], run3); // Up GERÇEKTEN derlendi (olay değil, invoke kanıtı) — Solo derlenmedi
            // Kontrol: pre-skip mekanizması bu run'da CANLI (yoksa Up'ın derlenmesi önemsiz olurdu).
            var skipped = Assert.Single(h.Events.OfType<ProjectSkippedEvent>());
            Assert.Equal(Id("Solo"), skipped.ProjectId);
            Assert.Equal("skipped — up to date", skipped.Reason);
            Assert.Equal(BuildResult.Succeeded, store.Load()[Id("Up")].LastResult); // yeşile dönünce kayıt düzelir
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    // ---------------------------------------------------------------- [T20-b/K11] perf: cap + priority

    /// <summary>
    /// [T20-b] Cap/priority çağrılarını SIRASIYLA yakalayan <see cref="ICpuGovernor"/>. Gerçek bir Win32 job
    /// yerine bu kullanılır: <c>QueryCpuRate</c> yalnız YÜRÜRLÜKTEKİ durumu görebilir, oysa buradaki iddialar
    /// "ne zaman ne uygulandı VE run sonunda geri alındı mı" hakkındadır — sıra ancak kaydedilerek görülür.
    /// </summary>
    private sealed class RecordingGovernor : ICpuGovernor
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls { get { lock (_calls) return [.. _calls]; } }

        /// <summary>[Fix round 1 — KÖK 2] Cap yazımını gerçek job'ın hata yolundaki gibi patlatır
        /// (<c>SetInformationJobObject</c> başarısızlığı <see cref="System.ComponentModel.Win32Exception"/>'dır).</summary>
        public bool FailCapWithWin32 { get; init; }

        public void ApplyCap(int? percent)
        {
            if (FailCapWithWin32) throw new System.ComponentModel.Win32Exception(87); // ERROR_INVALID_PARAMETER
            lock (_calls) _calls.Add(percent is { } p ? $"cap:{p}" : "cap:off");
        }

        public void ApplyPriority(ProcessPriorityClassKind kind)
        {
            lock (_calls) _calls.Add("prio:" + kind);
        }
    }

    /// <summary>[T20-b] Bir profilin governor'da bırakacağı İZ — yüzdeler testlerde LİTERAL yazılmaz
    /// (P1 review dersi): tek doğruluk kaynağı <see cref="PerfProfile"/> tablosudur.
    /// <para><b>İSTİSNA:</b> konsol satırının K11 KOPYA METNİ (<c>"cpu cap 40%"</c>) bilerek literaldir —
    /// türetilseydi kendi kendini doğrulayan bir totoloji olurdu. Bedeli: Light'ın cap'i ileride değişirse o
    /// assert "yanlış nedenle" kırılır; o gün metin de birlikte güncellenir.</para></summary>
    private static string[] Applied(PerfMode mode)
    {
        var p = PerfProfile.For(mode);
        return [p.CpuCapPercent is { } cap ? $"cap:{cap}" : "cap:off", "prio:" + p.Priority];
    }

    /// <summary>Run sonu geri alma izi = Full profili (cap yok + Normal priority).</summary>
    private static string[] Released() => Applied(PerfMode.Full);

    [Fact]
    public async Task Light_perf_mode_caps_the_inner_job_at_run_start_and_the_cap_is_released_when_the_run_ends()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start(parallelism: 2) with { PerfMode = "Light" }, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Light = %40 + Idle (PerfProfile tablosu); run bitince cap KALDIRILIR ve priority Normal'e döner —
        // inner job Supervisor ömrü boyunca yaşadığı için cap orada BIRAKILAMAZ.
        Assert.Equal([.. Applied(PerfMode.Light), .. Released()], governor.Calls);
        var started = Assert.Single(h.Events.OfType<RunStartedEvent>());
        Assert.Equal(PerfProfile.For(PerfMode.Light).CpuCapPercent, started.CpuCapPercent); // runStarted uygulanan cap'i taşır
        Assert.Equal(2, started.Parallelism);     // paralellik KOMUTTAN gelir, profilden yeniden türetilmez
        Assert.Contains(h.ConsoleLines, l => l.Contains("cpu cap 40%", StringComparison.Ordinal)); // run-başı satırı cap'i yazar
    }

    [Fact]
    public async Task Full_perf_mode_clears_the_cap_instead_of_setting_one()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Full" }, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([.. Applied(PerfMode.Full), .. Released()], governor.Calls);
        Assert.Null(Assert.Single(h.Events.OfType<RunStartedEvent>()).CpuCapPercent);
        Assert.Contains(h.ConsoleLines, l => l.Contains("cpu cap off", StringComparison.Ordinal));
    }

    // Geriye dönük uyum: PerfMode taşımayan (P2 öncesi ya da harness) komutlar job'a HİÇ dokunmamalı —
    // aksi halde bu değişiklik mevcut tüm run'lara sessizce bir priority/cap yazımı eklerdi. Run-başı satırı
    // da "off" DEMEZ ("kapatıldı" ≠ "hiç istenmedi").
    [Fact]
    public async Task A_start_command_without_a_perf_mode_never_touches_the_cpu_governor()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Empty(governor.Calls);
        Assert.Null(Assert.Single(h.Events.OfType<RunStartedEvent>()).CpuCapPercent);
        Assert.Contains(h.ConsoleLines, l => l.Contains("cpu cap unset", StringComparison.Ordinal));
    }

    [Fact]
    public void Perf_mode_change_without_an_active_run_is_a_no_op()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        h.Sut.ApplyPerfMode(PerfProfile.For(PerfMode.Light)); // koşan run yok → kısılacak MSBuild de yok

        Assert.Empty(governor.Calls);
    }

    // [Fix round 1 — KÖK 3] Kapının İKİNCİ dalı: run AKTİF ama bu run hiç PerfMode taşımıyor (P2 öncesi şekil).
    // Canlı setPerfMode o run'a uygulanmaz (cap sahibi yok, run sonunda geri alacak kimse yok) — ve niyet
    // SONRAKİ run'a da SIZMAZ. `if (!_runActive) return;` mutasyonu bu testte kırmızıya döner.
    [Fact]
    public async Task A_live_perf_change_during_a_run_that_declared_no_perf_mode_is_ignored_and_does_not_leak_to_the_next_run()
    {
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) =>
        {
            inFlight.TrySetResult();
            await release.Task;
            return Ok();
        });
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start(runId: "r1"), default); // PerfMode YOK
        await inFlight.Task.WaitAsync(Limit);
        h.Sut.ApplyPerfMode(PerfProfile.For(PerfMode.Light)); // run uçuşta, ama cap sahibi yok
        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);
        Assert.Empty(governor.Calls);

        // İkinci run (yine PerfMode'suz): bir önceki niyetin ARTIĞI uygulanmamalı.
        release = Signal();
        release.TrySetResult();
        await h.Sut.StartAsync(Start(runId: "r2"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);
        Assert.Empty(governor.Calls);
    }

    // [Fix round 1 — KÖK 1] Planlama penceresi: startRun kabul edildi ama plan hâlâ kuruluyor (177 projede
    // SANİYELER). Perf chip'i o pencerede canlıdır; gelen setPerfMode SESSİZCE KAYBOLMAMALI, run başlarken
    // komuttaki profili EZMELİDİR (kullanıcının SON niyeti).
    [Fact]
    public async Task A_perf_change_arriving_while_the_plan_is_still_being_built_wins_when_the_run_starts()
    {
        var planningStarted = Signal();
        var releasePlanning = Signal();
        var plan = PlanOf(Node("A"));
        RunPlan GatedPlanner(StartRunCommand _, Action<string> __)
        {
            planningStarted.TrySetResult();
            releasePlanning.Task.GetAwaiter().GetResult(); // run task'ını bloklar, test thread'ini DEĞİL [D8]
            return plan;
        }
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        var governor = new RecordingGovernor();
        using var h = new Harness(plan, invoker, planner: GatedPlanner, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Full" }, default);
        await planningStarted.Task.WaitAsync(Limit);
        h.Sut.ApplyPerfMode(PerfProfile.For(PerfMode.Light)); // henüz hiçbir şey uygulanmadı
        Assert.Empty(governor.Calls);                          // planlama sırasında job'a dokunulmaz
        releasePlanning.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([.. Applied(PerfMode.Light), .. Released()], governor.Calls); // Full DEĞİL Light uygulandı
        Assert.Equal(PerfProfile.For(PerfMode.Light).CpuCapPercent,
            Assert.Single(h.Events.OfType<RunStartedEvent>()).CpuCapPercent);
    }

    [Fact]
    public async Task Live_perf_mode_change_moves_the_cap_and_priority_but_never_the_worker_count()
    {
        // İki proje uçuşa girene kadar kapı kapalı: perf değişimi run'ın TAM ORTASINDA uygulanır (sleep/poll YOK [D8]).
        var plan = PlanOf(Node("A"), Node("B"), Node("C"), Node("D"));
        var pairInFlight = Signal();
        var release = Signal();
        int arrived = 0;
        var invoker = new FakeInvoker(async (_, _, _) =>
        {
            if (Interlocked.Increment(ref arrived) >= 2) pairInFlight.TrySetResult();
            await release.Task;
            return Ok();
        });
        var governor = new RecordingGovernor();
        using var h = new Harness(plan, invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start(parallelism: 2) with { PerfMode = "Light" }, default);
        await pairInFlight.Task.WaitAsync(Limit);

        // Balanced'ın paralelliği 4'tür — worker'lar run başında bir kez yaratıldığı için bu run'da 2 KALIR;
        // canlı değişen YALNIZ cap + priority'dir (K11'in dürüst yorumu).
        h.Sut.ApplyPerfMode(PerfProfile.For(PerfMode.Balanced));
        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([.. Applied(PerfMode.Light), .. Applied(PerfMode.Balanced), .. Released()], governor.Calls);
        Assert.Equal(2, invoker.MaxConcurrent); // worker sayısı DEĞİŞMEDİ
        Assert.Equal(4, h.Events.OfType<ProjectSucceededEvent>().Count());
    }

    // Cap, hard stop yolunda da geri alınmalı: orada job Terminate edilir ama JOB'IN KENDİSİ yaşamaya devam
    // eder (yeni process kabul eder) — cap kalsaydı bir sonraki run/Continue kısıtlı başlardı.
    [Fact]
    public async Task The_cap_is_released_on_the_hard_stop_path_too()
    {
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) =>
        {
            inFlight.TrySetResult();
            await release.Task;
            return Exit(1); // hard stop sonrası child ölür gibi
        });
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Light" }, default);
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Hard));
        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([.. Applied(PerfMode.Light), .. Released()], governor.Calls);
    }

    // [Fix round 2 — YENİ 1] Run'ın KAPANIŞ PENCERESİ: koordinatör cap'i geri aldı (ReleasePerf) ama son
    // event'ler hâlâ pump tarafından yazılıyor, yani `_runActive` HÂLÂ true. IPC dispatch loop'u ayrı bir
    // thread'dedir ve setPerfMode'un hiçbir run-state ön koşulu yoktur — o pencerede gelen bir niyet, sahibi
    // olmayan bir "pending" olarak kalıp BİR SONRAKİ run'ın profilini sessizce ezerdi.
    [Fact]
    public async Task A_perf_change_arriving_while_the_last_events_are_still_draining_does_not_leak_into_the_next_run()
    {
        var gate = new PumpGateStream("runCompleted");
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor, output: gate);

        await h.Sut.StartAsync(Start(runId: "r1"), default); // PerfMode YOK → job'a hiç dokunulmamalı
        await gate.Reached.WaitAsync(Limit);                 // runCompleted yazılırken duraklat
        h.Sut.ApplyPerfMode(PerfProfile.For(PerfMode.Light)); // run BİTTİ ama _runActive henüz true
        gate.Release();
        await h.Sut.RunCompletion.WaitAsync(Limit);
        Assert.Empty(governor.Calls);

        // İKİNCİ run, yine PerfMode'suz: bayat niyet UYGULANMAMALI (aksi halde kullanıcının bu run için
        // seçtiği profil ezilir ve cap'siz olması gereken bir run capli başlar).
        await h.Sut.StartAsync(Start(runId: "r2"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Empty(governor.Calls);
        Assert.All(h.Events.OfType<RunStartedEvent>(), e => Assert.Null(e.CpuCapPercent));
    }

    // [Fix round 2 — YENİ 4] Geri alma yazımı BAŞARISIZ olsa bile perf bayrağı run SINIRINI geçmemeli: aksi
    // halde PerfMode taşımayan bir SONRAKİ run, hiç dokunmadığı job'a run sonunda cap/priority yazardı ve
    // "PerfMode'suz run job'a HİÇ dokunmaz" invariant'ı koşullu hale gelirdi.
    [Fact]
    public async Task A_failed_release_does_not_carry_the_perf_flag_into_the_next_run()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        var governor = new RecordingGovernor { FailCapWithWin32 = true }; // hem apply hem release'in cap yazımı patlar
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start(runId: "r1") with { PerfMode = "Light" }, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        await h.Sut.StartAsync(Start(runId: "r2"), default); // PerfMode YOK
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // r1: priority uygulandı + geri alındı. r2: HİÇBİR yazım olmamalı (üçüncü bir "prio:Normal" YOK).
        Assert.Equal(["prio:Idle", "prio:Normal"], governor.Calls);
    }

    // [Fix round 1 — KÖK 2] Cap yazımı patlarsa priority yazımı YİNE denenmeli (iki yarı bağımsız değerlidir)
    // ve run ÖLMEMELİ; runStarted da olmayan bir cap'i RAPOR ETMEMELİ (yalnız GERÇEKTEN uygulanan taşınır).
    [Fact]
    public async Task A_failing_cap_write_still_lets_the_priority_write_through_and_is_reported_as_no_cap()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        var governor = new RecordingGovernor { FailCapWithWin32 = true };
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Light" }, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // cap çağrıları FIRLADI (kaydedilmedi) ama priority'ler hem run başında hem run sonunda yazıldı.
        Assert.Equal(["prio:Idle", "prio:Normal"], governor.Calls);
        Assert.Null(Assert.Single(h.Events.OfType<RunStartedEvent>()).CpuCapPercent);
        // [D1 review round 2] Konsol kanalındaki metin İngilizce'ye çevrildi (uygulama İngilizce-only) —
        // assert AYNI satırı pinlemeye devam eder, gevşetilmez.
        Assert.Contains(h.ConsoleLines, l => l.Contains("cpu cap could not be applied", StringComparison.Ordinal));
        Assert.Equal(RunOutcome.Completed, h.Events.OfType<RunCompletedEvent>().Single().Outcome); // run ÖLMEDİ
    }

    // ------------------------------------------- [T20-b/P3] copy fazı: cap taban değeri + graceful-stop drain

    /// <summary>Cap'i olmayan (Full / geri alınmış) hâlin <see cref="RecordingGovernor"/> izi.</summary>
    private const string CapOff = "cap:off";

    /// <summary>Bir profilin priority izi.</summary>
    private static string Prio(PerfMode mode) => "prio:" + PerfProfile.For(mode).Priority;

    /// <summary>Taban priority'sinin izi — cap yazımından BAĞIMSIZ olarak da gerekir: graceful drain'de cap
    /// kalkar ama priority tabana çekilir (bkz. <c>EffectivePriorityLocked</c>, final review I-1).</summary>
    private static string FloorPrio => "prio:" + PerfProfile.CopyPhaseFloorPriority;

    /// <summary>Copy-contention penceresinin izi: cap VE priority tabanı (ikisi de Balanced'dan türetilir —
    /// cap'i gevşetip priority'yi Idle'da bırakmak floor'un yarısını etkisiz kılardı).</summary>
    private static string[] FloorApplied() => [$"cap:{PerfProfile.CopyPhaseFloorPercent}", FloorPrio];

    /// <summary>Tek bir contention'ı senaryolayan invoker: 1. deneme MSB3021 + exit 1, sonraki denemeler OK.</summary>
    private static FakeInvoker ContendingOnce()
    {
        int attempts = 0;
        return new FakeInvoker((_, onLine, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1)
            {
                onLine(ContentionLine);
                return Task.FromResult(Exit(1));
            }
            return Task.FromResult(Ok());
        });
    }

    [Fact]
    public async Task A_copy_contention_lifts_the_cap_to_the_copy_phase_floor_and_puts_the_run_profile_back()
    {
        // Post-build copy MSBuild child'ının İÇİNDE olur; "copy başlıyor" sinyali yoktur. Elde olan tek sinyal
        // GERİYE DÖNÜKTÜR (MSB302x) — taban tam o pencerede uygulanır, retry bitince run profiline dönülür.
        var governor = new RecordingGovernor();
        var invoker = ContendingOnce();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Light" }, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(2, invoker.Requests.Count); // tetikleyici GERÇEKTEN ateşledi (retry oldu)
        Assert.Equal([.. Applied(PerfMode.Light), .. FloorApplied(), .. Applied(PerfMode.Light), .. Released()],
            governor.Calls);
        Assert.Equal(RunOutcome.Completed, h.Events.OfType<RunCompletedEvent>().Single().Outcome); // retry geçti
    }

    [Fact]
    public async Task A_copy_contention_under_the_uncapped_Full_profile_never_touches_the_cap()
    {
        // Cap yoksa gevşetilecek bir şey de yok: governor'a apply+release DIŞINDA hiçbir yazım gitmemeli.
        var governor = new RecordingGovernor();
        var invoker = ContendingOnce();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Full" }, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // POZİTİF KONTROL önce: contention gerçekten retry ürettiyse "dokunulmadı" iddiası anlamlıdır —
        // aksi halde tetikleyici bozulduğunda bu test sessizce yeşil kalırdı.
        Assert.Equal(2, invoker.Requests.Count);
        Assert.Equal([.. Applied(PerfMode.Full), .. Released()], governor.Calls);
    }

    [Fact]
    public async Task A_copy_contention_in_a_run_without_a_perf_mode_never_touches_the_governor()
    {
        var governor = new RecordingGovernor();
        var invoker = ContendingOnce();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start(), default); // PerfMode YOK → job'a hiç dokunulmaz
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(2, invoker.Requests.Count); // pozitif kontrol (yukarıdaki gerekçe)
        Assert.Empty(governor.Calls);
    }

    [Fact]
    public async Task The_capped_backoff_is_wired_to_the_live_run_profile()
    {
        // GERÇEK kablaj: RunCoordinator.IsCapActiveNow → CoordinatorCpuFloor.IsCapActive → decorator.
        // Decorator testlerindeki el yazması fake bu zinciri göremez; kablaj kopsa yalnız bu test kırılır.
        var invoker = ContendingOnce();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: new RecordingGovernor());

        await h.Sut.StartAsync(Start() with { PerfMode = "Light" }, default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(2, invoker.Requests.Count);
        Assert.Equal([TimeSpan.FromMilliseconds(300)], h.RetryDelays); // 200ms × 1.5 (cap aktif)
    }

    [Fact]
    public async Task The_backoff_is_not_stretched_in_a_run_without_a_perf_mode()
    {
        var invoker = ContendingOnce();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: new RecordingGovernor());

        await h.Sut.StartAsync(Start(), default); // cap yok → dizi AYNEN
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(2, invoker.Requests.Count);
        Assert.Equal([TimeSpan.FromMilliseconds(200)], h.RetryDelays);
    }

    [Fact]
    public async Task A_live_perf_change_during_a_copy_window_cannot_push_the_cap_or_priority_below_the_floor()
    {
        // Sıkışmış bir post-build copy, tam ortasında yeniden kısılamaz: pencere açıkken gelen setPerfMode
        // cap'i de priority'yi de TABAN'ın altına yazamaz (yeni profil ancak pencere kapanınca yürürlüğe girer).
        var retrying = Signal();
        var release = Signal();
        int attempts = 0;
        var invoker = new FakeInvoker(async (_, onLine, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1) { onLine(ContentionLine); return Exit(1); }
            retrying.TrySetResult();
            await release.Task;
            return Ok();
        });
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Light" }, default);
        await retrying.Task.WaitAsync(Limit); // pencere AÇIK

        h.Sut.ApplyPerfMode(PerfProfile.For(PerfMode.Light)); // cap:40 + prio:Idle isterdi
        Assert.Equal([.. Applied(PerfMode.Light), .. FloorApplied(), .. FloorApplied()], governor.Calls);

        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Pencere kapanınca profil (Light) yürürlüğe girer.
        Assert.Equal([.. Applied(PerfMode.Light), .. FloorApplied(), .. FloorApplied(),
            .. Applied(PerfMode.Light), .. Released()], governor.Calls);
    }

    [Fact]
    public async Task Two_workers_in_a_copy_window_lift_the_cap_once_and_restore_it_only_when_the_last_one_leaves()
    {
        // Paralel build'de İKİ worker aynı anda contention görebilir. Ref-count yoksa erken çıkan worker, diğeri
        // hâlâ kopyalarken cap'i geri kısar (izde cap:70·cap:70·cap:40·cap:40 görünürdü).
        var plan = PlanOf(Node("A"), Node("B"));
        var attemptsById = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int Attempt(string id) { lock (attemptsById) { attemptsById.TryGetValue(id, out int n); return attemptsById[id] = n + 1; } }

        var bothFirstAttempts = Signal();
        var bothRetrying = Signal();
        var releaseRetries = Signal();
        int firstArrived = 0, retryArrived = 0;
        var invoker = new FakeInvoker(async (req, onLine, _) =>
        {
            if (Attempt(req.ProjectId) == 1)
            {
                // İki projenin İLK denemesi de contention'a düşsün (randevu: ikisi de uçuşta) [D8].
                if (Interlocked.Increment(ref firstArrived) == 2) bothFirstAttempts.TrySetResult();
                await bothFirstAttempts.Task;
                onLine(ContentionLine);
                return Exit(1);
            }
            // 2. deneme = pencere AÇIK (Enter, backoff'tan ÖNCE olur) — ikisi de burada tutulur.
            if (Interlocked.Increment(ref retryArrived) == 2) bothRetrying.TrySetResult();
            await releaseRetries.Task;
            return Ok();
        });
        var governor = new RecordingGovernor();
        using var h = new Harness(plan, invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start(parallelism: 2) with { PerfMode = "Light" }, default);
        await bothRetrying.Task.WaitAsync(Limit);

        Assert.Equal([.. Applied(PerfMode.Light), .. FloorApplied()], governor.Calls); // İKİ pencere, TEK yükseltme

        releaseRetries.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([.. Applied(PerfMode.Light), .. FloorApplied(), .. Applied(PerfMode.Light), .. Released()],
            governor.Calls);
    }

    [Fact]
    public async Task A_graceful_stop_lifts_the_cap_so_the_in_flight_post_build_copy_can_drain()
    {
        // Graceful stop, in-flight child'ların post-build copy'lerini TAMAMLAMASINA dayanır ("ortak bin'de torn
        // DLL yok", §3). O pencereyi %40'lık bir HARD_CAP ile uzatmak garantiyi zamanlama yarışına çevirirdi.
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) => { inFlight.TrySetResult(); await release.Task; return Ok(); });
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Light" }, default);
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));

        // Cap ANINDA kalkar; priority'ye DOKUNULMAZ (Idle yalnız öncelik verir, HARD_CAP mutlak tavandır).
        Assert.Equal([.. Applied(PerfMode.Light), CapOff], governor.Calls);

        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);
        Assert.Equal([.. Applied(PerfMode.Light), CapOff, .. Released()], governor.Calls);
    }

    [Fact]
    public async Task A_graceful_stop_in_a_run_without_a_perf_mode_still_never_touches_the_governor()
    {
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) => { inFlight.TrySetResult(); await release.Task; return Ok(); });
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start(), default); // PerfMode YOK
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));
        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Empty(governor.Calls);
    }

    [Fact]
    public async Task A_graceful_stop_during_a_copy_window_is_not_undone_when_that_window_closes()
    {
        // Drain kararı run'ın GERİ KALANI için bağlayıcıdır: kapanan copy penceresi cap'i geri KOYAMAZ, aksi
        // halde drain'in tam ortasında job yeniden %40'a kısılırdı.
        var retrying = Signal();
        var release = Signal();
        int attempts = 0;
        var invoker = new FakeInvoker(async (_, onLine, _) =>
        {
            if (Interlocked.Increment(ref attempts) == 1) { onLine(ContentionLine); return Exit(1); }
            retrying.TrySetResult();
            await release.Task;
            return Ok();
        });
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Light" }, default);
        await retrying.Task.WaitAsync(Limit);
        Assert.Equal([.. Applied(PerfMode.Light), .. FloorApplied()], governor.Calls); // pencere açık

        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));
        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        // Pencere kapanışı hiçbir şey yazmaz — cap zaten kalkmıştır.
        Assert.Equal([.. Applied(PerfMode.Light), .. FloorApplied(), CapOff, .. Released()], governor.Calls);
    }

    [Fact]
    public async Task A_graceful_stop_binds_the_run_even_when_the_active_profile_has_no_cap()
    {
        // [Fix round 1] Drain KARARI cap'i olmayan (Full) profilde de kaydedilmelidir. Aksi halde bayrak hiç
        // set edilmez ve drain sürerken gelen canlı bir setPerfMode("Light") cap:40 yazarak "ortak bin'de torn
        // DLL yok" penceresini geri açardı — kaldıracak bir cap olmaması, kararın geçersiz olduğu anlamına gelmez.
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) => { inFlight.TrySetResult(); await release.Task; return Ok(); });
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Full" }, default);
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));
        Assert.Equal([.. Applied(PerfMode.Full)], governor.Calls); // cap yok → drain hiçbir şey YAZMAZ

        h.Sut.ApplyPerfMode(PerfProfile.For(PerfMode.Light)); // cap:40 + prio:Idle isterdi — drain kararı BAĞLAR
        // [final review I-1] Priority de bağlanır: Idle YAZILMAZ, taban (Balanced) yazılır.
        Assert.Equal([.. Applied(PerfMode.Full), CapOff, FloorPrio], governor.Calls);

        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([.. Applied(PerfMode.Full), CapOff, FloorPrio, .. Released()], governor.Calls);
    }

    /// <summary>
    /// [final review I-1] Drain kararı cap ile priority'yi AYNI ölçüde bağlar. Stop'a basıldıktan sonra perf
    /// chip'i CANLI kalır (<c>CanStop() = IsRunning || IsStarting</c>), yani kullanıcı beklerken profili
    /// değiştirebilir. Eskiden <c>EffectivePriorityLocked</c> <c>_capDrained</c>'e BAKMIYORDU: <c>Balanced</c>
    /// koşan bir run'ın in-flight <c>MSBuild.exe</c> child'ları drain'in tam ortasında <c>Idle</c>'a
    /// düşürülebiliyordu — yüklü bir makinede Idle bir child, tavanı serbest olsa bile zamanlayıcıdan sıra
    /// alamaz ve <c>MsBuildInvoker.PerProjectTimeout</c> (10 dk) aşılırsa child <c>Killed</c> edilir; yani
    /// drain'in korumaya çalıştığı yarım yazılmış çıktı senaryosu geri gelirdi.
    /// </summary>
    [Fact]
    public async Task A_perf_change_during_the_graceful_drain_can_lower_neither_the_cap_nor_the_priority()
    {
        var inFlight = Signal();
        var release = Signal();
        var invoker = new FakeInvoker(async (_, _, _) => { inFlight.TrySetResult(); await release.Task; return Ok(); });
        var governor = new RecordingGovernor();
        using var h = new Harness(PlanOf(Node("A")), invoker, cpuGovernor: governor);

        await h.Sut.StartAsync(Start() with { PerfMode = "Balanced" }, default);
        await inFlight.Task.WaitAsync(Limit);
        Assert.True(h.Sut.TryRequestStop(StopKind.Graceful));
        Assert.Equal([.. Applied(PerfMode.Balanced), CapOff], governor.Calls); // cap ANINDA kalkar

        h.Sut.ApplyPerfMode(PerfProfile.For(PerfMode.Light)); // cap:40 + prio:Idle isterdi

        // İki yarı da aynı karara bağlı: cap yok, priority tabanın ALTINA inmedi.
        Assert.Equal([.. Applied(PerfMode.Balanced), CapOff, CapOff, FloorPrio], governor.Calls);
        Assert.DoesNotContain(Prio(PerfMode.Light), governor.Calls); // "prio:Idle" HİÇ yazılmadı

        release.TrySetResult();
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([.. Applied(PerfMode.Balanced), CapOff, CapOff, FloorPrio, .. Released()], governor.Calls);
    }
}
