using System.IO;
using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
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
}
