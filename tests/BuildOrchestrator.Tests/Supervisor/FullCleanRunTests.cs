using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.MsBuild;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>
/// [Clean] Build menüsünün <b>Clean</b>'i: kapsamsız bir <c>RunMode.Clean</c> koşusu. Satır menüsündeki Clean bir
/// proje için ne yapıyorsa bu, grafın TÜM projeleri için aynısını yapar — her projede <c>msbuild -t:Clean</c>,
/// hiçbir şey derlenmez, temizlenen her projenin defter kaydı silinir. Kapsam grafta ne varsa odur: harici
/// projeler ve döngü üyeleri dahil. Clean'in bağımlılık anlamı yoktur (<c>Core.Planning.CleanRunScope</c>):
/// sıra beklenmez, derlemeye özgü kurallar (dep-issue) devreye girmez.
///
/// Fixture: <see cref="RunCoordinatorTests"/>'in harness'ı AYNEN (<c>using static</c>) — kopya YASAK.
/// </summary>
public class FullCleanRunTests
{
    private static StartRunCommand FullClean(int parallelism = 2) => Start(RunMode.Clean, parallelism);

    /// <summary>Sıradan <c>Lib</c>, ona bağlı harici <c>Ext</c> ve <c>Lib</c>'e bağlı bir döngü (<c>A ⇄ B</c>) —
    /// tam bir Build'in üyeleri "in dependency cycle" diye atladığı, hariciyi sıradan proje saydığı grafik.</summary>
    private static RunPlan GraphWithExternalAndCycle() => CyclePlanOf(["A", "B"],
        Node("Lib", willBuild: false),
        Node("Ext", deps: ["Lib"], willBuild: false) with { IsExternal = true },
        Node("A", deps: ["B", "Lib"], inCycle: true, willBuild: false),
        Node("B", deps: ["A"], inCycle: true, willBuild: false));

    private static List<string> LogTextsFor(Harness h, string name) => h.Events.OfType<ProjectLogEvent>()
        .Where(e => NameOf(e.ProjectId) == name).OrderBy(e => e.LineNumber).Select(e => e.Text).ToList();

    /// <summary>Grafta ne varsa temizlenir: döngü üyeleri önden atlanmaz (tur da koşmaz — Clean derlemez),
    /// harici proje sıradan bir proje gibi temizlenir. Her istek MSBuild'in Clean hedefidir.</summary>
    [Fact]
    public async Task Every_project_in_the_graph_is_cleaned_including_externals_and_cycle_members()
    {
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(GraphWithExternalAndCycle(), invoker);

        await h.Sut.StartAsync(FullClean(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["A", "B", "Ext", "Lib"], invoker.Requests.Select(r => NameOf(r.ProjectId)).Order());
        Assert.All(invoker.Requests, r => Assert.Equal(MsBuildTarget.Clean, r.Target));
        Assert.Empty(h.Events.OfType<ProjectSkippedEvent>());      // "in dependency cycle" pre-skip'i YOK
        Assert.Empty(h.Events.OfType<CycleRoundStartedEvent>());   // tur döngüsü YOK
        var done = Assert.Single(h.Events.OfType<RunCompletedEvent>());
        Assert.Equal((4, 0, 0, 0), (done.Succeeded, done.Failed, done.Skipped, done.Queued));
    }

    /// <summary>Satır Clean'inin defter kuralı her projeye uygulanır: çıktılar gittiğinde defter de onları
    /// bilmemeli (gerekçe <c>BuildStateStore.Remove</c>'da) — döngü üyesi ve harici proje dahil.</summary>
    [Fact]
    public async Task Every_cleaned_projects_ledger_row_is_forgotten()
    {
        string cacheRoot = NewCacheRoot();
        try
        {
            var store = new BuildStateStore(cacheRoot);
            foreach (string name in new[] { "Lib", "Ext", "A", "B" })
                store.Upsert(new BuildState(Id(name), "sig", "headsha", BuildResult.Succeeded));
            var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
            using var h = new Harness(GraphWithExternalAndCycle(), invoker, stateStore: store);

            await h.Sut.StartAsync(FullClean(), default);
            await h.Sut.RunCompletion.WaitAsync(Limit);

            Assert.Empty(store.Load());
        }
        finally { if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true); }
    }

    /// <summary>Clean bağımlılık sırası BEKLEMEZ: bir projenin temizliği başka bir projenin çıktısına ihtiyaç
    /// duymaz, dispatch yalnız plan sırasını izler. Plan sırası topolojik olmak zorunda değildir (katman
    /// bariyeri onu bozabilir — <see cref="BuildPlan.Nodes"/>); burada dependent bağımlılığından ÖNCE gelir ve
    /// tek worker'la ilk o temizlenir.</summary>
    [Fact]
    public async Task A_full_clean_does_not_wait_for_dependencies()
    {
        var plan = PlanOf(Node("Dependent", deps: ["Base"]), Node("Base"));
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(FullClean(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(["Dependent", "Base"], invoker.Requests.Select(r => NameOf(r.ProjectId)));
    }

    /// <summary>Derlemeye özgü dep-issue kuralı Clean'e SIZMAZ: patlayan bir Clean ("hata derlemeyi öldürmez")
    /// dependent'ın temizliğini "bayat çıktıya karşı derlendi" diye işaretlemez — log başında uyarı satırı,
    /// olayda <c>depIssues</c>, koşu özetinde dependency-affected sayısı YOKTUR.</summary>
    [Fact]
    public async Task A_failed_clean_gives_no_dependent_a_dependency_issue()
    {
        var plan = PlanOf(Node("Base"), Node("Dependent", deps: ["Base"]));
        var invoker = new FakeInvoker((req, _, _) =>
            Task.FromResult(NameOf(req.ProjectId) == "Base" ? Exit(1) : Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(FullClean(parallelism: 1), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        var ok = Assert.Single(h.Events.OfType<ProjectSucceededEvent>());
        Assert.Equal("Dependent", NameOf(ok.ProjectId));
        Assert.Null(ok.DepIssues);
        Assert.DoesNotContain(LogTextsFor(h, "Dependent"), line => line.StartsWith("warning:", StringComparison.Ordinal));
        Assert.Equal(0, Assert.Single(h.Events.OfType<RunCompletedEvent>()).DepIssueCount);
    }

    /// <summary>Önizleme BU koşunun işini anlatır: her proje temizlenecek (<c>WillBuild = true</c>) — App'in
    /// kuyruğu, ilerleme paydası ve açılış satırı bu kümeyi okur. Gerekçe bir disk olgusudur ve korunur; koşullu
    /// değerlendirme (derlemeye özgü) hiçbir projeye uygulanmaz.</summary>
    [Fact]
    public async Task The_preview_counts_every_project_as_this_runs_work_and_keeps_the_reasons()
    {
        var plan = CyclePlanOf(["A", "B"],
            Node("Current", willBuild: false) with { WillBuildReason = WillBuildReason.UpToDate },
            Node("Dirty", willBuild: true) with { WillBuildReason = WillBuildReason.SignatureChanged },
            Node("A", deps: ["B"], inCycle: true, willBuild: false) with { WillBuildReason = WillBuildReason.SignatureChanged },
            Node("B", deps: ["A"], inCycle: true, willBuild: false) with { WillBuildReason = WillBuildReason.UpToDate });
        var invoker = new FakeInvoker((_, _, _) => Task.FromResult(Ok()));
        using var h = new Harness(plan, invoker);

        await h.Sut.StartAsync(FullClean(), default);
        await h.Sut.RunCompletion.WaitAsync(Limit);

        Assert.Equal(4, Assert.Single(h.Events.OfType<RunStartedEvent>()).TotalProjects);
        var preview = Assert.Single(h.Events.OfType<BuildPreviewEvent>());
        Assert.All(preview.Items, i => Assert.True(i.WillBuild));
        Assert.All(preview.Items, i => Assert.False(i.Conditional));
        Assert.Equal(
            [WillBuildReason.UpToDate, WillBuildReason.SignatureChanged, WillBuildReason.SignatureChanged, WillBuildReason.UpToDate],
            preview.Items.Select(i => i.Reason));
    }
}
