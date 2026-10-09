namespace BuildOrchestrator.Tests.Planning;

using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;
using BuildOrchestrator.Core.Planning;
using Xunit;

/// <summary>[D5/D7] Yüzey kapısı: "bağımlılığının API yüzeyi değişmediyse derleme". Aday seçimi iki plandan okunur —
/// Safe (bu koşunun kararı) ve Fast (frozen-upstream: upstream'lerin DEFTERDEKİ imzasıyla hesaplanan, cascade'siz
/// karar). Fast "güncel" diyorsa projenin kendi terimi (içerik + configuration) değişmemiştir ve kanıtı sağlamdır;
/// Safe "imza değişti" diyorsa kirlilik yalnız bir upstream'dendir. Böyle proje sırası gelince kapıdan geçer.</summary>
public class SurfaceGateTests
{
    private static ProjectNode Node(string id, bool? willBuild, WillBuildReason? reason, bool inCycle = false, params string[] deps) =>
        new ProjectNode(id, id, id, [], deps, 0, null, null, inCycle, willBuild) with { WillBuildReason = reason };

    private static BuildPlan Plan(params ProjectNode[] nodes) => new(nodes, Cycles: [], Configuration: "Debug");

    [Theory]
    [InlineData(RunMode.Build, false, true)]
    [InlineData(RunMode.Cycles, false, true)]
    [InlineData(RunMode.Rebuild, false, false)]
    [InlineData(RunMode.Clean, false, false)]
    [InlineData(RunMode.Build, true, false)]
    public void the_gate_applies_to_full_runs_that_follow_the_ledger(RunMode mode, bool scoped, bool expected)
        => Assert.Equal(expected, SurfaceGate.AppliesTo(mode, scoped));

    [Fact]
    public void a_project_dirty_only_through_an_upstream_is_a_candidate()
    {
        var safe = Plan(Node("U", true, WillBuildReason.SignatureChanged), Node("D", true, WillBuildReason.SignatureChanged, deps: ["U"]));
        var fast = Plan(Node("U", true, WillBuildReason.SignatureChanged), Node("D", false, WillBuildReason.UpToDate, deps: ["U"]));

        Assert.Equal(["D"], SurfaceGate.CandidateIds(safe, fast).Order());
    }

    [Theory]
    [InlineData(WillBuildReason.SignatureChanged, false, WillBuildReason.BuiltOutside)] // kanıt zaman kipinde: Fast UpToDate demedi
    [InlineData(WillBuildReason.NeverBuilt, true, WillBuildReason.NeverBuilt)]           // kendi sebebiyle kirli
    [InlineData(WillBuildReason.WaitingForDependency, true, WillBuildReason.WaitingForDependency)] // kök bekleyen: ConditionalRebuild'in işi
    // Fast de "değişti" dedi: configuration ya da kendi içeriği değişti — YA DA upstream daha önceki bir koşuda
    // derlendi ve defterdeki imzası yeni (Review Focus 4: bağımlı bir kez koşulsuz derlenir, güvenli yön).
    [InlineData(WillBuildReason.SignatureChanged, true, WillBuildReason.SignatureChanged)]
    public void other_reasons_are_not_candidates(WillBuildReason safeReason, bool? fastWillBuild, WillBuildReason fastReason)
    {
        var safe = Plan(Node("U", true, WillBuildReason.SignatureChanged), Node("D", true, safeReason, deps: ["U"]));
        var fast = Plan(Node("U", true, WillBuildReason.SignatureChanged), Node("D", fastWillBuild, fastReason, deps: ["U"]));

        Assert.Empty(SurfaceGate.CandidateIds(safe, fast));
    }

    [Fact] // doğrudan bağımlılığı olmayan proje ve döngü üyesi aday değildir (üyenin yolu CycleMemberNeed'dir)
    public void roots_and_cycle_members_are_never_candidates()
    {
        var safe = Plan(Node("R", true, WillBuildReason.SignatureChanged),
                        Node("M", true, WillBuildReason.SignatureChanged, inCycle: true, deps: ["R"]));
        var fast = Plan(Node("R", false, WillBuildReason.UpToDate),
                        Node("M", false, WillBuildReason.UpToDate, inCycle: true, deps: ["R"]));

        Assert.Empty(SurfaceGate.CandidateIds(safe, fast));
    }
}
