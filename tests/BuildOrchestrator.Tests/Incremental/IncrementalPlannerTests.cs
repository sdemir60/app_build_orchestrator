using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests.Incremental;

// [T25][A6] IncrementalPlanner — GLOBAL graf propagation (Safe = dirty + transitive dependents; Fast = yalnız
// dirty, cascade yok) + skip gate. BuildSignature (Task 6) + BuildStateStore (Task 2) + WillBuildEvaluator/
// BuildPreview (mevcut, değişmez) seam'ine bağlanır. Testler senkron/in-memory git facts enjekte eder (gerçek
// repo gerekmez): headCommit, per-node dirty dosya listesi, state dictionary.
public class IncrementalPlannerTests
{
    private static ProjectNode Node(string id, int buildOrder, bool inCycle, params string[] dependencies) =>
        new(id, id, id, [], dependencies, buildOrder, null, null, InCycle: inCycle, WillBuild: null);

    private static readonly Func<string, string> NoRead = _ => throw new InvalidOperationException("okunmamalıydı");

    private static Func<string, string> ContentMap(params (string Path, string Content)[] entries)
    {
        var map = entries.ToDictionary(e => e.Path, e => e.Content, StringComparer.Ordinal);
        return path => map.TryGetValue(path, out var c) ? c : throw new KeyNotFoundException(path);
    }

    private static Dictionary<string, IReadOnlyList<string>> Dirty(params (string Id, string[] Files)[] entries) =>
        entries.ToDictionary(e => e.Id, e => (IReadOnlyList<string>)e.Files, StringComparer.OrdinalIgnoreCase);

    private static Func<ProjectNode, IReadOnlyList<string>> DirtyLookup(IReadOnlyDictionary<string, IReadOnlyList<string>> map) =>
        node => map.TryGetValue(node.Id, out var files) ? files : [];

    // ---- L1 -> L2 -> L3 chain: kök dirty, Safe TÜM zincire yayılır, Fast yalnız kökte kalır -----------------

    [Fact]
    public void chain_root_dirty_file_propagates_to_all_transitive_dependents_in_safe_mode()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        // Baseline: L1 önceden temiz içerikle (v1) başarıyla derlenmiş; zincir tutarlı imzalarla state'e yazılmış.
        var readV1 = ContentMap(("L1.cs", "v1"));
        string sigL1Old = BuildSignature.Compute(l1, "Debug", "headA", [], readV1, _ => null, inPlace: true);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "headA", [], NoRead, id => id == "L1" ? sigL1Old : null, inPlace: true);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "headA", [], NoRead, id => id == "L2" ? sigL2Old : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1Old, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3Old, LastResult: BuildResult.Succeeded),
        };

        // Şimdi: L1.cs working-tree'de dirty, içerik v2'ye değişti; L2/L3'ün KENDİ dosyaları temiz.
        var readV2 = ContentMap(("L1.cs", "v2"));
        var dirty = DirtyLookup(Dirty(("L1", ["L1.cs"])));

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, headCommit: "headA", dirtyFilesForNode: dirty, readFileContent: readV2,
            state: state, inPlace: true, mode: DependentMode.Safe);

        Assert.True(result.Nodes.Single(n => n.Id == "L1").WillBuild);
        Assert.True(result.Nodes.Single(n => n.Id == "L2").WillBuild); // transitive propagation
        Assert.True(result.Nodes.Single(n => n.Id == "L3").WillBuild); // transitive propagation
    }

    [Fact]
    public void chain_root_dirty_file_does_not_cascade_in_fast_mode_only_root_rebuilds()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        var readV1 = ContentMap(("L1.cs", "v1"));
        string sigL1Old = BuildSignature.Compute(l1, "Debug", "headA", [], readV1, _ => null, inPlace: true);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "headA", [], NoRead, id => id == "L1" ? sigL1Old : null, inPlace: true);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "headA", [], NoRead, id => id == "L2" ? sigL2Old : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1Old, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3Old, LastResult: BuildResult.Succeeded),
        };

        var readV2 = ContentMap(("L1.cs", "v2"));
        var dirty = DirtyLookup(Dirty(("L1", ["L1.cs"])));

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, headCommit: "headA", dirtyFilesForNode: dirty, readFileContent: readV2,
            state: state, inPlace: true, mode: DependentMode.Fast);

        Assert.True(result.Nodes.Single(n => n.Id == "L1").WillBuild);
        Assert.False(result.Nodes.Single(n => n.Id == "L2").WillBuild); // no cascade
        Assert.False(result.Nodes.Single(n => n.Id == "L3").WillBuild); // no cascade
    }

    [Fact]
    public void never_built_root_propagates_in_safe_but_not_fast()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var plan = new BuildPlan([l1, l2], [], "Debug");

        // L2 daha önce başarıyla derlenmiş (L1'in o zamanki -yok- imzasıyla tutarlı: upstream null idi).
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "headA", [], NoRead, _ => null, inPlace: true);
        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            // L1 hiç state'e girmemiş (never-built) — kayıt yok.
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
        };

        var noDirty = DirtyLookup(Dirty());

        var safe = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", noDirty, NoRead, state, inPlace: true, mode: DependentMode.Safe);
        Assert.True(safe.Nodes.Single(n => n.Id == "L1").WillBuild); // never-built
        Assert.True(safe.Nodes.Single(n => n.Id == "L2").WillBuild); // upstream (L1) imzası artık farklı (gerçek vs null) -> propagate

        var fast = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", noDirty, NoRead, state, inPlace: true, mode: DependentMode.Fast);
        Assert.True(fast.Nodes.Single(n => n.Id == "L1").WillBuild);  // never-built (own)
        Assert.False(fast.Nodes.Single(n => n.Id == "L2").WillBuild); // frozen upstream = stored (null) -> own unchanged -> no cascade
    }

    // ---- Config değişimi: TÜM projeler dirty --------------------------------------------------------------

    [Fact]
    public void configuration_switch_marks_every_project_dirty_in_safe_mode()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var planDebug = new BuildPlan([l1, l2, l3], [], "Debug");

        string sigL1 = BuildSignature.Compute(l1, "Debug", "headA", [], NoRead, _ => null, inPlace: true);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "headA", [], NoRead, id => id == "L1" ? sigL1 : null, inPlace: true);
        string sigL3 = BuildSignature.Compute(l3, "Debug", "headA", [], NoRead, id => id == "L2" ? sigL2 : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3, LastResult: BuildResult.Succeeded),
        };

        // Aynı state (Debug'da kaydedilmiş), şimdi Release ile aynen aynı (temiz, dirty yok) plan çalıştırılıyor.
        var planRelease = planDebug with { Configuration = "Release" };
        var noDirty = DirtyLookup(Dirty());

        var result = IncrementalPlanner.ComputeWillBuild(
            planRelease, "headA", noDirty, NoRead, state, inPlace: true, mode: DependentMode.Safe);

        Assert.True(result.Nodes.Single(n => n.Id == "L1").WillBuild);
        Assert.True(result.Nodes.Single(n => n.Id == "L2").WillBuild);
        Assert.True(result.Nodes.Single(n => n.Id == "L3").WillBuild);
    }

    [Fact]
    public void configuration_switch_also_marks_every_project_dirty_in_fast_mode_since_config_is_an_own_term()
    {
        // Config, upstream propagation ile DEĞİL doğrudan kendi (own) imza teriminden gelir — bu yüzden Fast'in
        // "cascade yok" kuralı config değişimini maskelemez: frozen-upstream ile bile own-term (cfg) farklıdır.
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var planDebug = new BuildPlan([l1, l2], [], "Debug");

        string sigL1 = BuildSignature.Compute(l1, "Debug", "headA", [], NoRead, _ => null, inPlace: true);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "headA", [], NoRead, id => id == "L1" ? sigL1 : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2, LastResult: BuildResult.Succeeded),
        };

        var planRelease = planDebug with { Configuration = "Release" };
        var noDirty = DirtyLookup(Dirty());

        var result = IncrementalPlanner.ComputeWillBuild(
            planRelease, "headA", noDirty, NoRead, state, inPlace: true, mode: DependentMode.Fast);

        Assert.True(result.Nodes.Single(n => n.Id == "L1").WillBuild);
        Assert.True(result.Nodes.Single(n => n.Id == "L2").WillBuild);
    }

    // ---- Temiz + Succeeded + imza eşit -> skip (dalga dalga, sıra korunur) --------------------------------

    [Fact]
    public void clean_chain_with_matching_signatures_and_succeeded_state_skips_every_project_in_build_order()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        string sigL1 = BuildSignature.Compute(l1, "Debug", "headA", [], NoRead, _ => null, inPlace: true);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "headA", [], NoRead, id => id == "L1" ? sigL1 : null, inPlace: true);
        string sigL3 = BuildSignature.Compute(l3, "Debug", "headA", [], NoRead, id => id == "L2" ? sigL2 : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3, LastResult: BuildResult.Succeeded),
        };

        var noDirty = DirtyLookup(Dirty());

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", noDirty, NoRead, state, inPlace: true, mode: DependentMode.Safe);

        // Sıra korunur (build-order) ve her biri KENDİ konumunda, önceki düğümün skip kararına bağlı biçimde
        // (topological/memoized) hesaplanır — hepsi tek seferde/toptan değil.
        Assert.Equal(["L1", "L2", "L3"], result.Nodes.Select(n => n.Id));
        Assert.All(result.Nodes, n => Assert.False(n.WillBuild));
    }

    // ---- inCycle -> her zaman false, sinyal/state ne olursa olsun ------------------------------------------

    [Fact]
    public void cycle_members_never_build_regardless_of_signature_or_state()
    {
        var a = Node("A", 0, inCycle: true, "B");
        var b = Node("B", 1, inCycle: true, "A");
        var plan = new BuildPlan([a, b], [["A", "B"]], "Debug");

        var noDirty = DirtyLookup(Dirty());

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", noDirty, NoRead, new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase),
            inPlace: true, mode: DependentMode.Safe);

        Assert.False(result.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.False(result.Nodes.Single(n => n.Id == "B").WillBuild);
    }

    // ---- hollow / pre-Sync: headCommit null -> WillBuild null tüm plan için ------------------------------

    [Fact]
    public void null_head_commit_yields_hollow_null_will_build_for_every_project()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var plan = new BuildPlan([l1, l2], [], "Debug");

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, headCommit: null, dirtyFilesForNode: DirtyLookup(Dirty()), readFileContent: NoRead,
            state: new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase), inPlace: true, mode: DependentMode.Safe);

        Assert.Null(result.Nodes.Single(n => n.Id == "L1").WillBuild);
        Assert.Null(result.Nodes.Single(n => n.Id == "L2").WillBuild);
    }

    // ---- Diamond: A -> {B, C} -> D; ortak upstream (A) dirty -> her iki kol + join Safe'te yayılır -------

    [Fact]
    public void diamond_shared_upstream_dirty_propagates_through_both_branches_and_the_join_in_safe_mode()
    {
        var a = Node("A", 0, inCycle: false);
        var b = Node("B", 1, inCycle: false, "A");
        var c = Node("C", 2, inCycle: false, "A");
        var d = Node("D", 3, inCycle: false, "B", "C");
        var plan = new BuildPlan([a, b, c, d], [], "Debug");

        var readV1 = ContentMap(("A.cs", "v1"));
        string sigAOld = BuildSignature.Compute(a, "Debug", "headA", [], readV1, _ => null, inPlace: true);
        string sigBOld = BuildSignature.Compute(b, "Debug", "headA", [], NoRead, id => id == "A" ? sigAOld : null, inPlace: true);
        string sigCOld = BuildSignature.Compute(c, "Debug", "headA", [], NoRead, id => id == "A" ? sigAOld : null, inPlace: true);
        string sigDOld = BuildSignature.Compute(d, "Debug", "headA", [], NoRead,
            id => id == "B" ? sigBOld : id == "C" ? sigCOld : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new BuildState("A", sigAOld, LastResult: BuildResult.Succeeded),
            ["B"] = new BuildState("B", sigBOld, LastResult: BuildResult.Succeeded),
            ["C"] = new BuildState("C", sigCOld, LastResult: BuildResult.Succeeded),
            ["D"] = new BuildState("D", sigDOld, LastResult: BuildResult.Succeeded),
        };

        var readV2 = ContentMap(("A.cs", "v2"));
        var dirty = DirtyLookup(Dirty(("A", ["A.cs"])));

        var safe = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", dirty, readV2, state, inPlace: true, mode: DependentMode.Safe);

        Assert.True(safe.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "B").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "C").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "D").WillBuild); // join, iki koldan da propagate

        var fast = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", dirty, readV2, state, inPlace: true, mode: DependentMode.Fast);

        Assert.True(fast.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "B").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "C").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "D").WillBuild);
    }
}
