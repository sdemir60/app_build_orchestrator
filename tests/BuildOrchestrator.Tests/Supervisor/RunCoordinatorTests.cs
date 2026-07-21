using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Logs;
using BuildOrchestrator.Core.MsBuild;
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

    private sealed class Harness : IDisposable
    {
        private readonly MemoryStream _out = new();
        private readonly List<RunLogWriter> _logWriters = [];
        private long _now;

        public JobObject Job { get; } = JobObject.CreateKillOnClose();
        public string LogsRoot { get; } = Directory.CreateTempSubdirectory("bo-coord-").FullName;
        public List<string> ConsoleLines { get; } = [];
        public RunCoordinator Sut { get; }
        public IReadOnlyList<RunLogWriter> LogWriters { get { lock (_logWriters) return [.. _logWriters]; } }

        public Harness(RunPlan plan, FakeInvoker invoker, Func<StartRunCommand, RunPlan>? planner = null,
            Func<StartRunCommand, string?>? worktreeObjRootResolver = null, BuildStateStore? stateStore = null)
        {
            Sut = new RunCoordinator(
                planner: planner ?? (_ => plan),
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
                stateStore: stateStore);
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
        Func<StartRunCommand, RunPlan> blockingPlanner = _ =>
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

        Assert.Equal(["runStarted", "buildPreview", "projectSkipped:X", "projectSkipped:Y", "runCompleted:Completed"],
            received.Select(Describe));
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
        // [I2-K2/Task 10] worktree'nin GERÇEK hazırlanması (WorktreeManager.PrepareWorktreeAsync) bu run
        // akışına henüz bağlı DEĞİL (Program.cs planner'ı yalnız cmd.RootPath/Configuration alır) — burada
        // worktreeObjRootResolver enjekte edilerek "worktree kökü biliniyorsa" davranış test edilir.
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
    public async Task worktree_run_without_a_resolver_still_passes_null_obj_path() // deferred wiring: Program.cs henüz resolver vermiyor
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
    // restore artığı — Reason metnindeki "yabancı TFM" ifadesi test boyunca stale-warn işareti olarak kullanılır.
    private const string StaleMarker = "yabancı TFM";

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
                Incremental: Incremental("Dirty"));
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
            Assert.DoesNotContain(store.Load().Values, s => s.ProjectId == Id("Clean")); // derlenmedi → persist yok
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }
}
