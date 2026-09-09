using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [tek proje · design v1.11.0 §3.8] Satırdan tetiklenen koşu: <c>startRun</c> bir <c>scopeProjectId</c>
/// taşır ve koordinatör planı TEK düğüme indirger. Kapsam dışına dokunulmaz — diğer projeler koşuya hiç
/// girmez (skip satırı yok, sayaç yok). Hedef tam koşuyla AYNI motor yolundan geçer: Build modunda
/// incremental kural, Rebuild'de koşulsuz; bayat bağımlılıklar dep-issue olarak hedefe yapışır.
///
/// Fixture: <see cref="RunCoordinatorTests"/>'in harness'ı AYNEN (<c>using static</c>) — kopya YASAK.
/// </summary>
public class SingleProjectRunTests
{
    private static StartRunCommand Scoped(string target, RunMode mode = RunMode.Build, string runId = "r1") =>
        Start(mode, parallelism: 2, runId) with { ScopeProjectId = Id(target) };

    private static List<string> LogTextsFor(Harness h, string name) => h.Events.OfType<ProjectLogEvent>()
        .Where(e => NameOf(e.ProjectId) == name).OrderBy(e => e.LineNumber).Select(e => e.Text).ToList();

    [Fact]
    public async Task Only_the_target_enters_the_run_and_the_others_are_never_mentioned()
    {
        var plan = PlanOf(
            Node("Base", willBuild: true),
            Node("Target", deps: ["Base"], willBuild: true),
            Node("Dependent", deps: ["Target"], willBuild: true));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Scoped("Target"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([Id("Target")], invoker.Requests.Select(r => r.ProjectId)); // yalnız hedef derlendi
        var started = Assert.Single(h.Events.OfType<RunStartedEvent>());
        Assert.Equal(1, started.TotalProjects);
        var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>());
        Assert.Equal([Id("Target")], preview.Items.Select(i => i.ProjectId));
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>()); // kapsam dışı için skip satırı YOK
        var done = Assert.Single(h.Events.OfType<RunCompletedEvent>());
        Assert.Equal((1, 0, 0, 0), (done.Succeeded, done.Failed, done.Skipped, done.Queued));
        Assert.Contains("scope: single project Target", h.DecisionLog);
    }

    /// <summary>
    /// <b>Hedef güncel olsa da derlenir</b> — satırdaki play bir emirdir (design §3.8 "koşul yok").
    /// <para><b>[DEĞİŞEN KURAL — ölçüldü]</b> Eski iddia: kapsamlı Build tam koşunun incremental kuralına
    /// tabiydi ve güncel bir hedefi <c>skipped — up to date</c> ile atlardı. Sahada bunun anlamı şuydu: ilk
    /// basış derliyor, ikinci basış hiçbir şey yapmadan satırı gri bırakıyor — kullanıcı bunu "satırdan build
    /// bazen gri kalıyor" diye bildirdi. Gerekçe <see cref="Core.Planning.ProjectRunScope"/>'ta.</para>
    /// </summary>
    [Fact]
    public async Task A_scoped_build_compiles_its_target_even_when_it_is_up_to_date()
    {
        var plan = PlanOf(Node("Target", willBuild: false), Node("Other", willBuild: true));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Scoped("Target", RunMode.Build, "r1"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());                     // "up to date" atlaması YOK
        Assert.Equal([Id("Target")], invoker.Requests.Select(r => r.ProjectId));  // GERÇEKTEN derlendi
        Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
        var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>());
        Assert.True(Assert.Single(preview.Items).WillBuild);                      // önizleme de "derlenecek" der
    }

    /// <summary>
    /// Menünün iki maddesi FARKLI şeyler yapar: <b>Build</b> projeyi derler (<c>-t:Build</c>), <b>Rebuild</b>
    /// MSBuild'in kendi Rebuild hedefini koşar (<c>-t:Rebuild</c> = önce Clean, sonra Build) — prototipin
    /// satır menüsü de tam olarak bunu yazar (<c>msbuild X.csproj /t:Rebuild</c>).
    /// <para><b>Alt bardaki Rebuild BUNDAN AYRIDIR ve değişmez:</b> orada "Rebuild" cache'i yok saymak
    /// demektir (proje başına yine <c>-t:Build</c>) — tek projelik bir kapsamda cache'i yok saymayı zaten
    /// Build yapıyor, dolayısıyla satırdaki Rebuild'in ayrı bir anlamı olmalıdır.</para>
    /// </summary>
    [Fact]
    public async Task A_scoped_rebuild_runs_msbuilds_rebuild_target_while_a_full_rebuild_still_builds()
    {
        var plan = PlanOf(Node("Target", willBuild: false), Node("Other", willBuild: false));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Scoped("Target", RunMode.Rebuild, "r1"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(MsBuildTarget.Rebuild, Assert.Single(invoker.Requests).Target);
        Assert.Contains("-t:Rebuild", MsBuildArguments.PlanFor(invoker.Requests[0]).Build);
        // Proje logunun İLK satırı gerçek komut satırıdır (v7Δ-7) — hedef oraya da yansır.
        Assert.Contains("-t:Rebuild", LogTextsFor(h, "Target")[0]);

        await h.Sut.StartAsync(Start(RunMode.Rebuild, parallelism: 1, "r2"), default); // ALT BARDAKİ Rebuild
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.All(invoker.Requests.Skip(1), r => Assert.Equal(MsBuildTarget.Build, r.Target));
    }

    /// <summary>
    /// Bayat (kirli) bir bağımlılık bu koşuda derlenmez — hedef onun son bilinen çıktısına karşı derlenir.
    /// Bu bir dep-issue'dur: log başında uyarı satırı, event'te <c>depIssues</c>, defterde NOT. Not olmasa
    /// hedefin taze imzası (upstream terimi bağımlılığın YENİ kaynağını zaten içerir) bir sonraki Build'i
    /// "güncel" diye atlatır ve proje kalıcı olarak bayat bir DLL'e link'li kalırdı (CycleRunScope'un deliği).
    /// </summary>
    [Fact]
    public async Task A_stale_dependency_becomes_a_dep_issue_with_a_warn_line_and_a_note_in_the_ledger()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            var plan = new RunPlan(
                new BuildPlan([Node("Clean", willBuild: false) with { BuildOrder = 0 },
                               Node("Dirty", willBuild: true) with { BuildOrder = 1 },
                               Node("Target", deps: ["Clean", "Dirty"], willBuild: true) with { BuildOrder = 2 }],
                    Cycles: [], Configuration: "Debug"),
                EmptyRefs(), Incremental: RunCoordinatorTests.Incremental("Clean", "Dirty", "Target")); // ad alanıyla (Tests.Incremental) çakışır
            var invoker = new FakeInvoker((req, onLine, _) => { onLine("real output"); return Task.FromResult(Ok()); });
            using var h = new Harness(plan, invoker, stateStore: store);

            await h.Sut.StartAsync(Scoped("Target"), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            var ok = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
            Assert.Equal(["Dirty"], ok.DepIssues);                       // Clean güncel — issue değil
            var log = LogTextsFor(h, "Target");
            Assert.Equal("warning: Dirty has pending changes and was not rebuilt in this run — last known output referenced", log[1]);
            Assert.Equal("real output", log[2]);
            var done = Assert.Single(h.Events.OfType<RunCompletedEvent>());
            Assert.Equal(1, done.DepIssueCount);

            var state = Assert.Contains(Id("Target"), store.Load());
            Assert.True(state.DepIssue, "bayat bağımlılığa karşı derlenen başarı NOTLA yazılmalı");
            Assert.Equal("sig", state.BuiltSignature);
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>Döngü üyesi bir hedef satırından TEK BAŞINA derlenir (tam Build onu atlardı); döngüdeki
    /// bağımlılıkları bayat sayılır ve uyarı döngü sözcükleriyle yazılır.</summary>
    [Fact]
    public async Task A_cycle_member_target_is_built_alone_against_its_cycle_mates_last_known_outputs()
    {
        var plan = CyclePlanOf(["A", "B"],
            Node("Lib", willBuild: false),
            Node("A", deps: ["B", "Lib"], inCycle: true, willBuild: false),
            Node("B", deps: ["A"], inCycle: true, willBuild: false));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Scoped("A"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal([Id("A")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());           // "in dependency cycle" pre-skip'i YOK
        Assert.Empty(h.Events.OfType<CycleRoundStartedEvent>());        // tur döngüsü YOK — tek proje
        var ok = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
        Assert.Equal(["B"], ok.DepIssues);
        Assert.Equal("warning: B is in a dependency cycle and was not rebuilt — last known output referenced",
            LogTextsFor(h, "A")[1]);
    }

    /// <summary>Planda olmayan bir hedef (bayat topoloji: proje silinmiş/taşınmış) koşuyu HİÇ başlatmaz —
    /// mevcut planlama-hatası kanalı (<c>planFailed</c>) kullanılır, App onu zaten tanır.</summary>
    [Fact]
    public async Task A_target_missing_from_the_plan_ends_the_run_as_planFailed_before_it_starts()
    {
        var plan = PlanOf(Node("A"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(Scoped("Gone"), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var error = Assert.Single(h.Events.OfType<ErrorEvent>());
        Assert.Equal("planFailed", error.Code);
        Assert.Contains("Gone", error.Message);
        Assert.Empty(h.Events.OfType<RunStartedEvent>());
        Assert.Empty(invoker.Requests);
    }
}
