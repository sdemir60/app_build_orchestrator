using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>[D7] Yüzey kapısının koşu içi davranışı. Fixture: U → D (D aday; isteğe bağlı kök R → U → D, Cycles koşusu için
/// U → D → A ↔ B). Yüzeyler <see cref="CycleRoundsTests.SurfaceDisk"/>'ten
/// (apiSurface seam), kanıt yolları <see cref="CycleRoundsTests.HashModePlan"/> ile — döngü testleriyle AYNI sahte disk (kopya YASAK).
/// <para><b>Ölçüm (gerçek OSYS, 2026-10-09, paralellik 1):</b> <c>OSYS.Types.General</c>'da gövde değişikliği sonrası Build'de plan
/// 143 projeyi kirli gördü; 7'si derlendi (1 asıl, 5'i SDK-style <c>Types.PRM</c>'in okunamayan yüzeyinden, 1'i patlayan bağımlılıktan),
/// 102'si kapıdan atlandı, 31 cycle üyesi taşındı — koşu 19 sn (kapısız kuralın kayıtlı sürelerle kaba tahmini 520 sn). API
/// değişikliğinde okuyan proje derlendi (ayrıntı: .claude/outputs/2026-10-09-12-42-surface-gate-measurement.md).</para></summary>
public class SurfaceGateRunTests : IDisposable
{
    private readonly string _cacheRoot = NewCacheRoot();
    public void Dispose() { if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true); }

    /// <summary>U → D, ikisi de "imza değişti" ile kirli; <paramref name="candidate"/> D'yi kapı adayı yapar. <paramref name="root"/>
    /// zincirin başına aynı biçimde kirli bir kök koyar: R → U → D.</summary>
    private static RunPlan UpDown(bool candidate = true, bool root = false)
    {
        string[] chain = root ? ["R", "U", "D"] : ["U", "D"];
        var plan = new RunPlan(new BuildPlan(
            [.. chain.Select((name, i) => Node(name, deps: i == 0 ? null : [chain[i - 1]], willBuild: true)
                with { BuildOrder = i, WillBuildReason = WillBuildReason.SignatureChanged })],
            Cycles: [], Configuration: "Debug"), EmptyRefs());
        plan = CycleRoundsTests.HashModePlan(plan, chain);
        return candidate ? AsCandidate(plan) : plan;
    }

    /// <summary>D'yi kapı adayı yapar (planlayıcının Safe/Fast karşılaştırmasının sonucu).</summary>
    private static RunPlan AsCandidate(RunPlan plan) =>
        plan with { Incremental = plan.Incremental! with { SurfaceCandidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Id("D") } } };

    /// <summary>Plandaki her düğüme yeni planlama imzası: kaynak koşular arasında değişti (sonraki koşunun girdisi).</summary>
    private static RunPlan Resigned(RunPlan plan, string signature) =>
        plan with
        {
            Incremental = plan.Incremental! with
            {
                SignatureById = plan.Plan.Nodes.ToDictionary(n => n.Id, _ => signature, StringComparer.OrdinalIgnoreCase),
            },
        };

    /// <summary>Aynı defter ve sahte disk üzerinde bir Build koşusu — döngü testlerinin koşturucusu (<see cref="CycleRoundsTests.ResolveAsync"/>)
    /// mod parametresiyle; ikinci bir koşturucu yazılmaz.</summary>
    private static Task<Harness> BuildAsync(BuildStateStore store, CycleRoundsTests.SurfaceDisk disk, RunPlan plan,
        FakeInvoker invoker, RunMode mode = RunMode.Build) =>
        CycleRoundsTests.ResolveAsync(store, disk, plan, invoker, mode: mode);

    /// <summary>[D8] Aday satır sıradan kirli satırdır: önizlemede Conditional=false, dalga/kuyruk/payda onu sayar; karar
    /// sırası gelince verilir ve sonucu (skipped — up to date) sayımı ilerletir.</summary>
    [Fact]
    public async Task a_surface_candidate_is_previewed_as_a_plain_dirty_project()
    {
        var disk = new CycleRoundsTests.SurfaceDisk();
        using var h = await BuildAsync(new BuildStateStore(_cacheRoot), disk, UpDown(), AllSucceed());
        var d = Assert.Single(h.Events.OfType<BuildPreviewEvent>()).Items.Single(i => i.ProjectId == Id("D"));
        Assert.Equal((true, false), (d.WillBuild, d.Conditional));
    }

    /// <summary>Koşu 1: ikisi de derlenir, D'nin kaydı U'nun yüzeyini taşır. Koşu 2 (U yine kirli, yüzeyi AYNI): U derlenir, D sırası
    /// gelince atlanır — "skipped — up to date (no dependency surface changed)", kaydı yeni imzayla yenilenir, yüzeyler aynen.</summary>
    [Fact]
    public async Task a_dependent_is_skipped_when_its_dependency_recompiled_with_the_same_surface()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        var first = AllSucceed();
        using (var h = await BuildAsync(store, disk, UpDown(candidate: false), first))
            Assert.Equal([Id("U"), Id("D")], first.Requests.Select(r => r.ProjectId));
        var dBefore = store.Load()[Id("D")];
        Assert.Equal([new CycleReadSurface(Id("U"), CycleRoundsTests.SurfaceDisk.PathOf("U"), "u1")], dBefore.DependencySurfaces);

        var second = AllSucceed();
        using var run = await BuildAsync(store, disk, Resigned(UpDown(), "sig2"), second);
        Assert.Equal([Id("U")], second.Requests.Select(r => r.ProjectId));
        var skipped = Assert.Single(run.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal((Id("D"), SkipReasons.UpToDate), (skipped.ProjectId, skipped.Reason));
        Assert.Contains($"D: skipped — {SkipReasons.UpToDate} ({SurfaceGate.UnchangedDetail})", run.DecisionLog, StringComparison.Ordinal);
        var d = store.Load()[Id("D")];
        Assert.Equal(("sig2", BuildResult.Succeeded, dBefore.LastDurationMs), (d.BuiltSignature, d.LastResult, d.LastDurationMs));
        Assert.Equal(dBefore.DependencySurfaces, d.DependencySurfaces);
        Assert.Equal(1, Assert.Single(run.Events.OfType<RunCompletedEvent>()).Skipped);
    }

    /// <summary>Son sonucu başarı olmayan kayıt kapıdan geçmez (CycleMemberNeed kural iii'nin aynası). Senaryo: D başarılı (U'nun u1
    /// yüzeyiyle) → D düzenlenir, kanıtlı derleyici hatası (FailedSignature) → kaynak geri alınır → U yine kirli, yüzeyi AYNI.
    /// Defterin "kaynak geri alındı" kuralı Fast planda D'yi "güncel" okuduğu için D aday olabilir; kapı atlasaydı kayıt
    /// LastResult=Failed + FailedSignature ile yenilenir, D'yi kök bekleyen bağımlılar bir basış daha "still failing" okurdu.</summary>
    [Fact]
    public async Task a_dependent_whose_last_result_is_not_a_success_is_built()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        store.Upsert(store.Load()[Id("D")] with
        {
            LastResult = BuildResult.Failed, FailedSignature = "sig-broken", FailedAt = DateTimeOffset.UtcNow,
        });

        var invoker = AllSucceed();
        using var run = await BuildAsync(store, disk, UpDown(), invoker);
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Empty(run.Events.OfType<ProjectSkippedEvent>());
        var d = store.Load()[Id("D")];
        Assert.Equal((BuildResult.Succeeded, (string?)null), (d.LastResult, d.FailedSignature));
    }

    [Fact] // yüzey oynadı ⇒ D derlenir, kaydı yeni yüzeyi taşır
    public async Task a_dependent_is_built_when_its_dependency_surface_moved()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        var invoker = new FakeInvoker((req, _, _) =>
        {
            if (NameOf(req.ProjectId) == "U") disk.Set("U", "u2");
            return Task.FromResult(Ok());
        });
        using var run = await BuildAsync(store, disk, UpDown(), invoker);
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Empty(run.Events.OfType<ProjectSkippedEvent>());
        Assert.Equal("u2", Assert.Single(store.Load()[Id("D")].DependencySurfaces!).Hash);
    }

    [Fact] // bağımlılık bu koşuda patladı ⇒ D derlenir (dep-issue yolu, bugünkü gibi)
    public async Task a_dependent_is_built_with_a_dependency_issue_when_its_dependency_failed()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        var invoker = new FakeInvoker((req, _, _) => Task.FromResult(NameOf(req.ProjectId) == "U" ? Exit(1) : Ok()));
        using var run = await BuildAsync(store, disk, UpDown(), invoker);
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
        Assert.NotNull(Assert.Single(run.Events.OfType<ProjectSucceededEvent>()).DepIssues);
    }

    [Fact] // bağımlılık güncel diye pre-skip edildi ama satırdan derlenip yüzeyi değişmişti ⇒ diskten okunur ⇒ D derlenir
    public async Task a_skipped_dependency_surface_is_read_from_disk_not_assumed_unchanged()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        disk.Set("U", "u2"); // koşular arasında U'nun çıktısı değişti (satırdan derlendi)
        var plan = UpDown() with
        {
            Plan = UpDown().Plan with
            {
                Nodes = [.. UpDown().Plan.Nodes.Select(n => n.Name == "U" ? n with { WillBuild = false, WillBuildReason = WillBuildReason.UpToDate } : n)],
            },
        };
        var invoker = AllSucceed();
        using var run = await BuildAsync(store, disk, plan, invoker);
        Assert.Equal([Id("D")], invoker.Requests.Select(r => r.ProjectId));
    }

    /// <summary>Rebuild kapıyı uygulamaz; kapı yalnız defteri dinleyen koşularda. Rebuild'in başarısı yine bağımlılık yüzeylerini
    /// yazar: bir sonraki Build'in kapı tabanıdır (kaydın yeni imzası onu bu derlemenin yazdığını gösterir).</summary>
    [Fact]
    public async Task a_rebuild_ignores_the_gate()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        var invoker = AllSucceed();
        using var run = await BuildAsync(store, disk, Resigned(UpDown(), "sig2"), invoker, RunMode.Rebuild);
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
        var d = store.Load()[Id("D")];
        Assert.Equal("sig2", d.BuiltSignature);
        Assert.Equal([new CycleReadSurface(Id("U"), CycleRoundsTests.SurfaceDisk.PathOf("U"), "u1")], d.DependencySurfaces);
    }

    /// <summary>Kapıdan atlanan proje bağımlılık notunu miras alır: kök R bu koşuda patladı, U derlendi (R'ye link'li, notlu) ve
    /// yüzeyi değişmedi ⇒ D atlanır ama kaydı notu ve R kökünü taşır — D hâlâ R'nin bayat çıktısına dolaylı link'lidir, R
    /// düzelince yeniden derlenmeli. Koşu 1'de herkes temiz derlendi: notu yazan atlamanın kendisidir.</summary>
    [Fact]
    public async Task a_gate_skip_records_the_inherited_dependency_issue()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false, root: true), AllSucceed())) { }
        Assert.False(store.Load()[Id("D")].DepIssue);

        var invoker = new FakeInvoker((req, _, _) => Task.FromResult(NameOf(req.ProjectId) == "R" ? Exit(1) : Ok()));
        using var run = await BuildAsync(store, disk, Resigned(UpDown(root: true), "sig2"), invoker);
        Assert.Equal([Id("R"), Id("U")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Contains($"D: skipped — {SkipReasons.UpToDate} ({SurfaceGate.UnchangedDetail})", run.DecisionLog, StringComparison.Ordinal);
        var d = store.Load()[Id("D")];
        Assert.Equal(("sig2", BuildResult.Succeeded, true), (d.BuiltSignature, d.LastResult, d.DepIssue));
        Assert.Equal([Id("R")], d.DepIssueRoots);
    }

    /// <summary>Bağımlılığın yüzeyi listelenemezse (kanıt yolu türetilemedi) kayıt <c>null</c> taşır, boş liste DEĞİL — eski kayıtla
    /// aynı "yüzey kanıtı yok" hâli.</summary>
    [Fact]
    public async Task a_dependency_without_a_surface_leaves_the_record_without_surfaces()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        var plan = UpDown(candidate: false);
        plan = plan with { Incremental = plan.Incremental! with { OutputsById = CycleRoundsTests.SurfaceDisk.OutputsFor("D") } };
        var invoker = AllSucceed();
        using (await BuildAsync(store, disk, plan, invoker)) { }
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
        var d = store.Load()[Id("D")];
        Assert.Equal(BuildResult.Succeeded, d.LastResult);
        Assert.Null(d.DependencySurfaces);
    }

    /// <summary>U → D → A ↔ B (A, D'yi okur): U ve D "imza değişti" ile kirli; grubun terimleri A = <paramref name="termA"/>, B = b1.
    /// Cycles kapsamı grup + transitif upstream'idir: U ve D kapsamdadır.</summary>
    private static RunPlan UpDownIntoCycle(string signature, string termA)
    {
        var plan = CycleRoundsTests.MemberSkipPlan(CyclePlanOf(["A", "B"],
                Node("U", willBuild: true) with { WillBuildReason = WillBuildReason.SignatureChanged },
                Node("D", deps: ["U"], willBuild: true) with { WillBuildReason = WillBuildReason.SignatureChanged },
                Node("A", deps: ["B", "D"], inCycle: true), Node("B", deps: ["A"], inCycle: true)),
            signature, ("A", termA), ("B", "b1"));
        plan = plan with { Incremental = plan.Incremental! with { OutputsById = CycleRoundsTests.SurfaceDisk.OutputsFor("U", "D", "A", "B") } };
        return Resigned(plan, signature);
    }

    /// <summary>Kapı Cycles koşusunda da uygulanır (defteri dinleyen tam koşu). Koşu 1 Build: herkes derlenir. Koşu 2 Cycles: U yeniden
    /// derlenir ve yüzeyi aynı kalır ⇒ aday D sırası gelince atlanır; grup turunu koşar (A'nın kendi terimi değişti, B taşınır).</summary>
    [Fact]
    public async Task the_gate_applies_in_a_cycles_run()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        foreach (string name in new[] { "U", "D", "A", "B" }) disk.Set(name, name.ToLowerInvariant() + "1");
        var first = AllSucceed();
        using (await BuildAsync(store, disk, UpDownIntoCycle("sig1", "a1"), first)) { }
        Assert.Equal([Id("U"), Id("D"), Id("A"), Id("B")], first.Requests.Select(r => r.ProjectId));

        var invoker = AllSucceed();
        using var run = await BuildAsync(store, disk, AsCandidate(UpDownIntoCycle("sig2", "a2")), invoker, RunMode.Cycles);
        Assert.Equal([Id("U"), Id("A")], invoker.Requests.Select(r => r.ProjectId));
        Assert.Equal(SkipReasons.UpToDate, Assert.Single(run.Events.OfType<ProjectSkippedEvent>(), e => e.ProjectId == Id("D")).Reason);
        Assert.Contains($"D: skipped — {SkipReasons.UpToDate} ({SurfaceGate.UnchangedDetail})", run.DecisionLog, StringComparison.Ordinal);
        Assert.Equal(CycleOutcome.Converged, Assert.Single(run.Events.OfType<CycleCompletedEvent>()).Outcome);
    }
}
