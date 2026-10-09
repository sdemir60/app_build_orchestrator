using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Core.State;
using BuildOrchestrator.Supervisor;
using static BuildOrchestrator.Tests.Supervisor.RunCoordinatorTests;

namespace BuildOrchestrator.Tests.Supervisor;

/// <summary>[D7] Yüzey kapısının koşu içi davranışı. Fixture: U → D (D aday). Yüzeyler <see cref="CycleRoundsTests.SurfaceDisk"/>'ten
/// (apiSurface seam), kanıt yolları <see cref="CycleRoundsTests.HashModePlan"/> ile — döngü testleriyle AYNI sahte disk (kopya YASAK).</summary>
public class SurfaceGateRunTests : IDisposable
{
    private readonly string _cacheRoot = NewCacheRoot();
    public void Dispose() { if (Directory.Exists(_cacheRoot)) Directory.Delete(_cacheRoot, recursive: true); }

    /// <summary>U → D, ikisi de "imza değişti" ile kirli; <paramref name="candidate"/> D'yi kapı adayı yapar.</summary>
    private static RunPlan UpDown(bool candidate = true)
    {
        var plan = new RunPlan(new BuildPlan(
            [Node("U", willBuild: true) with { BuildOrder = 0, WillBuildReason = WillBuildReason.SignatureChanged },
             Node("D", deps: ["U"], willBuild: true) with { BuildOrder = 1, WillBuildReason = WillBuildReason.SignatureChanged }],
            Cycles: [], Configuration: "Debug"), EmptyRefs());
        plan = CycleRoundsTests.HashModePlan(plan, "U", "D");
        return candidate
            ? plan with { Incremental = plan.Incremental! with { SurfaceCandidateIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Id("D") } } }
            : plan;
    }

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
        var plan = UpDown() with
        {
            Incremental = UpDown().Incremental! with
            {
                SignatureById = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Id("U")] = "sig2", [Id("D")] = "sig2" },
            },
        };
        using var run = await BuildAsync(store, disk, plan, second);
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

    [Fact] // Rebuild kapıyı uygulamaz; kapı yalnız defteri dinleyen koşularda
    public async Task a_rebuild_ignores_the_gate()
    {
        var store = new BuildStateStore(_cacheRoot);
        var disk = new CycleRoundsTests.SurfaceDisk();
        disk.Set("U", "u1");
        using (await BuildAsync(store, disk, UpDown(candidate: false), AllSucceed())) { }
        var invoker = AllSucceed();
        using var run = await BuildAsync(store, disk, UpDown(), invoker, RunMode.Rebuild);
        Assert.Equal([Id("U"), Id("D")], invoker.Requests.Select(r => r.ProjectId));
    }
}
