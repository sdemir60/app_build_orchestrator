using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests.Incremental;

// [T25][A6] IncrementalPlanner — GLOBAL graf propagation (Safe = dirty + transitive dependents; Fast = yalnız
// dirty, cascade yok) + skip gate. BuildSignature (Task 6) + BuildStateStore (Task 2) + WillBuildEvaluator/
// BuildPreview (mevcut, değişmez) seam'ine bağlanır. Testler senkron/in-memory git facts enjekte eder (gerçek
// repo gerekmez): headCommit (yalnız hollow kapısı — [A6 refinement/Task 7b] proje imzasına DOĞRUDAN girmez),
// per-node PER-PROJECT committedFingerprint (bkz. ComputeCommittedFingerprint testleri altta), per-node dirty
// dosya listesi, state dictionary.
public class IncrementalPlannerTests
{
    private static ProjectNode Node(string id, int buildOrder, bool inCycle, params string[] dependencies) =>
        new(id, id, id, [], dependencies, buildOrder, null, null, InCycle: inCycle, WillBuild: null);

    private static readonly Func<string, string> NoRead = _ => throw new InvalidOperationException("okunmamalıydı");
    private static readonly Func<ProjectNode, string?> NoFingerprint =
        _ => throw new InvalidOperationException("okunmamalıydı");

    private static Func<string, string> ContentMap(params (string Path, string Content)[] entries)
    {
        var map = entries.ToDictionary(e => e.Path, e => e.Content, StringComparer.Ordinal);
        return path => map.TryGetValue(path, out var c) ? c : throw new KeyNotFoundException(path);
    }

    private static Dictionary<string, IReadOnlyList<string>> Dirty(params (string Id, string[] Files)[] entries) =>
        entries.ToDictionary(e => e.Id, e => (IReadOnlyList<string>)e.Files, StringComparer.OrdinalIgnoreCase);

    private static Func<ProjectNode, IReadOnlyList<string>> DirtyLookup(IReadOnlyDictionary<string, IReadOnlyList<string>> map) =>
        node => map.TryGetValue(node.Id, out var files) ? files : [];

    // [A6 refinement — Task 7b] Per-project committed fingerprint enjeksiyonu — headCommit'in eski GLOBAL
    // rolünün yerini alır. Testler gerçek bir git repo/GitService.GetTrackedBlobHashesAsync KULLANMAZ (D8 —
    // repo-free): her projeye ayrı bir opak fingerprint token'ı doğrudan atanır.
    private static Dictionary<string, string?> Fingerprints(params (string Id, string? Fingerprint)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Fingerprint, StringComparer.OrdinalIgnoreCase);

    private static Func<ProjectNode, string?> FingerprintLookup(IReadOnlyDictionary<string, string?> map) =>
        node => map.TryGetValue(node.Id, out var fp) ? fp : null;

    // ---- L1 -> L2 -> L3 chain: kök dirty, Safe TÜM zincire yayılır, Fast yalnız kökte kalır -----------------

    [Fact]
    public void chain_root_dirty_file_propagates_to_all_transitive_dependents_in_safe_mode()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        // Baseline: L1 önceden temiz içerikle (v1) başarıyla derlenmiş; zincir tutarlı imzalarla state'e yazılmış.
        // Committed fingerprint'ler bu testte SABİT kalır — senaryo bir COMMIT değil, working-tree dirty'sidir.
        var fp = Fingerprints(("L1", "fpL1"), ("L2", "fpL2"), ("L3", "fpL3"));
        var readV1 = ContentMap(("L1.cs", "v1"));
        string sigL1Old = BuildSignature.Compute(l1, "Debug", "fpL1", [], readV1, _ => null, inPlace: true);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2", [], NoRead, id => id == "L1" ? sigL1Old : null, inPlace: true);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "fpL3", [], NoRead, id => id == "L2" ? sigL2Old : null, inPlace: true);

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
            committedFingerprintForNode: FingerprintLookup(fp), state: state, inPlace: true, mode: DependentMode.Safe);

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

        var fp = Fingerprints(("L1", "fpL1"), ("L2", "fpL2"), ("L3", "fpL3"));
        var readV1 = ContentMap(("L1.cs", "v1"));
        string sigL1Old = BuildSignature.Compute(l1, "Debug", "fpL1", [], readV1, _ => null, inPlace: true);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2", [], NoRead, id => id == "L1" ? sigL1Old : null, inPlace: true);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "fpL3", [], NoRead, id => id == "L2" ? sigL2Old : null, inPlace: true);

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
            committedFingerprintForNode: FingerprintLookup(fp), state: state, inPlace: true, mode: DependentMode.Fast);

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
        var fp = Fingerprints(("L1", "fpL1"), ("L2", "fpL2"));
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2", [], NoRead, _ => null, inPlace: true);
        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            // L1 hiç state'e girmemiş (never-built) — kayıt yok.
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
        };

        var noDirty = DirtyLookup(Dirty());

        var safe = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", noDirty, NoRead, FingerprintLookup(fp), state, inPlace: true, mode: DependentMode.Safe);
        Assert.True(safe.Nodes.Single(n => n.Id == "L1").WillBuild); // never-built
        Assert.True(safe.Nodes.Single(n => n.Id == "L2").WillBuild); // upstream (L1) imzası artık farklı (gerçek vs null) -> propagate

        var fast = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", noDirty, NoRead, FingerprintLookup(fp), state, inPlace: true, mode: DependentMode.Fast);
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

        var fp = Fingerprints(("L1", "fpL1"), ("L2", "fpL2"), ("L3", "fpL3"));
        string sigL1 = BuildSignature.Compute(l1, "Debug", "fpL1", [], NoRead, _ => null, inPlace: true);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "fpL2", [], NoRead, id => id == "L1" ? sigL1 : null, inPlace: true);
        string sigL3 = BuildSignature.Compute(l3, "Debug", "fpL3", [], NoRead, id => id == "L2" ? sigL2 : null, inPlace: true);

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
            planRelease, "headA", noDirty, NoRead, FingerprintLookup(fp), state, inPlace: true, mode: DependentMode.Safe);

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

        var fp = Fingerprints(("L1", "fpL1"), ("L2", "fpL2"));
        string sigL1 = BuildSignature.Compute(l1, "Debug", "fpL1", [], NoRead, _ => null, inPlace: true);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "fpL2", [], NoRead, id => id == "L1" ? sigL1 : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2, LastResult: BuildResult.Succeeded),
        };

        var planRelease = planDebug with { Configuration = "Release" };
        var noDirty = DirtyLookup(Dirty());

        var result = IncrementalPlanner.ComputeWillBuild(
            planRelease, "headA", noDirty, NoRead, FingerprintLookup(fp), state, inPlace: true, mode: DependentMode.Fast);

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

        var fp = Fingerprints(("L1", "fpL1"), ("L2", "fpL2"), ("L3", "fpL3"));
        string sigL1 = BuildSignature.Compute(l1, "Debug", "fpL1", [], NoRead, _ => null, inPlace: true);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "fpL2", [], NoRead, id => id == "L1" ? sigL1 : null, inPlace: true);
        string sigL3 = BuildSignature.Compute(l3, "Debug", "fpL3", [], NoRead, id => id == "L2" ? sigL2 : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3, LastResult: BuildResult.Succeeded),
        };

        var noDirty = DirtyLookup(Dirty());

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", noDirty, NoRead, FingerprintLookup(fp), state, inPlace: true, mode: DependentMode.Safe);

        // Not: sıranın korunması (aşağıdaki Assert.Equal) BuildPreview.ComputeWillBuild'in plan.Nodes üzerinde
        // sırayı BOZMAYAN bir Select yapmasının doğrudan yapısal sonucudur — imza hesabının KANITI DEĞİLDİR.
        // Asıl kanıt burada: [A1] planner upstream imzalarını DFS + memo (+ on-stack guard) ile TALEP ÜZERİNE,
        // özyinelemeli hesaplar — "yalnız DAHA ÖNCE işlenmiş düğümlerin imzası okunabilir" diye bir kısıt
        // YOKTUR (plan.Nodes topolojik sıralı olmayabilir) — üç düğümün de "skip" (WillBuild=false) çıkması,
        // her düğümün upstream teriminin DOĞRU (taze, state'teki ile birebir eşleşen) değerle beslendiğinin
        // kanıtıdır.
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
        var fp = Fingerprints(("A", "fpA"), ("B", "fpB"));

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", noDirty, NoRead, FingerprintLookup(fp),
            new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase),
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

        // NoFingerprint throw eder eğer çağrılırsa — hollow kısa devresi committedFingerprintForNode'u HİÇ
        // çağırmamalı (bkz. IncrementalPlanner.ComputeWillBuild "headCommit is null" dalı).
        var result = IncrementalPlanner.ComputeWillBuild(
            plan, headCommit: null, dirtyFilesForNode: DirtyLookup(Dirty()), readFileContent: NoRead,
            committedFingerprintForNode: NoFingerprint,
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

        var fp = Fingerprints(("A", "fpA"), ("B", "fpB"), ("C", "fpC"), ("D", "fpD"));
        var readV1 = ContentMap(("A.cs", "v1"));
        string sigAOld = BuildSignature.Compute(a, "Debug", "fpA", [], readV1, _ => null, inPlace: true);
        string sigBOld = BuildSignature.Compute(b, "Debug", "fpB", [], NoRead, id => id == "A" ? sigAOld : null, inPlace: true);
        string sigCOld = BuildSignature.Compute(c, "Debug", "fpC", [], NoRead, id => id == "A" ? sigAOld : null, inPlace: true);
        string sigDOld = BuildSignature.Compute(d, "Debug", "fpD", [], NoRead,
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
            plan, "headA", dirty, readV2, FingerprintLookup(fp), state, inPlace: true, mode: DependentMode.Safe);

        Assert.True(safe.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "B").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "C").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "D").WillBuild); // join, iki koldan da propagate

        var fast = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", dirty, readV2, FingerprintLookup(fp), state, inPlace: true, mode: DependentMode.Fast);

        Assert.True(fast.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "B").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "C").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "D").WillBuild);
    }

    // ---- [A6 refinement — Task 7b] COMMIT GRANULARITY: bir commit yalnız BİR projenin dosyasını değiştirirse,
    // yalnız O proje (+ Safe'te transitive dependent'leri) dirty olur — ilişkisiz projeler DOKUNULMAZ. Bu,
    // eski repo-GLOBAL headCommit tasarımının ALTINDA imkansızdı (tek bir headCommit değişimi HER düğümün
    // "own" teriminde doğrudan yer aldığı için TÜM projeleri, ilişkisiz olsalar bile, dirty işaretlerdi) — bu
    // testler yeni committedFingerprintForNode enjeksiyonu OLMADAN (eski API'de) ifade DAHİ edilemez; yani bu
    // dosyanın geri kalanı derlenebilir hale gelmeden önce bu iki test RED'di (API'de committedFingerprintForNode
    // parametresi yoktu — derleme hatası). Şimdi (GREEN): -------------------------------------------------

    [Fact]
    public void commit_changing_only_a_leaf_projects_committed_file_does_not_dirty_unrelated_upstream_projects()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        // Baseline: L1/L2/L3, hepsi önceden clean + Succeeded olarak state'e yazılmış (kendi eski committed
        // fingerprint'leriyle tutarlı imzalar).
        string sigL1Old = BuildSignature.Compute(l1, "Debug", "fpL1-old", [], NoRead, _ => null, inPlace: true);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2-old", [], NoRead, id => id == "L1" ? sigL1Old : null, inPlace: true);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "fpL3-old", [], NoRead, id => id == "L2" ? sigL2Old : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1Old, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3Old, LastResult: BuildResult.Succeeded),
        };

        // Şimdi: repo'da YENİ bir commit var (repo-GLOBAL HEAD "headA" -> "headB" değişti — bu KASITLI: global
        // HEAD değişse dahi ilişkisiz projeler dirty OLMAMALI, bu testin tam kanıtladığı şey budur), ama commit
        // YALNIZ L3'ün kendi dosyasını değiştiriyor: L3'ün committed fingerprint'i farklı; L1/L2'ninki AYNEN
        // eskisi gibi. Working-tree'de hiçbir şey dirty değil (noDirty) — bu saf bir commit senaryosu.
        var fp = Fingerprints(("L1", "fpL1-old"), ("L2", "fpL2-old"), ("L3", "fpL3-NEW"));
        var noDirty = DirtyLookup(Dirty());

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, headCommit: "headB", dirtyFilesForNode: noDirty, readFileContent: NoRead,
            committedFingerprintForNode: FingerprintLookup(fp), state: state, inPlace: true, mode: DependentMode.Safe);

        Assert.False(result.Nodes.Single(n => n.Id == "L1").WillBuild); // ilişkisiz — commit onu ETKİLEMEDİ
        Assert.False(result.Nodes.Single(n => n.Id == "L2").WillBuild); // ilişkisiz — commit onu ETKİLEMEDİ
        Assert.True(result.Nodes.Single(n => n.Id == "L3").WillBuild);  // yalnız BU projenin dosyası değişti
    }

    [Fact]
    public void commit_changing_only_the_root_projects_committed_file_propagates_in_safe_but_not_in_fast()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        string sigL1Old = BuildSignature.Compute(l1, "Debug", "fpL1-old", [], NoRead, _ => null, inPlace: true);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2-old", [], NoRead, id => id == "L1" ? sigL1Old : null, inPlace: true);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "fpL3-old", [], NoRead, id => id == "L2" ? sigL2Old : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1Old, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3Old, LastResult: BuildResult.Succeeded),
        };

        // Commit YALNIZ L1'in (kökün) dosyasını değiştiriyor — L2/L3'ünki aynı.
        var fp = Fingerprints(("L1", "fpL1-NEW"), ("L2", "fpL2-old"), ("L3", "fpL3-old"));
        var noDirty = DirtyLookup(Dirty());

        var safe = IncrementalPlanner.ComputeWillBuild(
            plan, headCommit: "headB", dirtyFilesForNode: noDirty, readFileContent: NoRead,
            committedFingerprintForNode: FingerprintLookup(fp), state: state, inPlace: true, mode: DependentMode.Safe);

        Assert.True(safe.Nodes.Single(n => n.Id == "L1").WillBuild); // kendi commit'i değişti
        Assert.True(safe.Nodes.Single(n => n.Id == "L2").WillBuild); // transitive propagation
        Assert.True(safe.Nodes.Single(n => n.Id == "L3").WillBuild); // transitive propagation

        var fast = IncrementalPlanner.ComputeWillBuild(
            plan, headCommit: "headB", dirtyFilesForNode: noDirty, readFileContent: NoRead,
            committedFingerprintForNode: FingerprintLookup(fp), state: state, inPlace: true, mode: DependentMode.Fast);

        Assert.True(fast.Nodes.Single(n => n.Id == "L1").WillBuild);  // kendi commit'i değişti
        Assert.False(fast.Nodes.Single(n => n.Id == "L2").WillBuild); // frozen upstream -> cascade yok
        Assert.False(fast.Nodes.Single(n => n.Id == "L3").WillBuild); // frozen upstream -> cascade yok
    }

    // ---- ComputeCommittedFingerprint: GitService tracked-blob map ∩ projenin build-etkileyen dosyaları -----

    [Fact]
    public void ComputeCommittedFingerprint_returns_null_when_no_project_file_intersects_the_tracked_blob_map()
    {
        var trackedBlobHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Other/File.cs"] = "aaaa",
        };

        string? fp = IncrementalPlanner.ComputeCommittedFingerprint(trackedBlobHashes, ["L1.cs"]);

        Assert.Null(fp); // proje hiç commit'lenmemiş (map'te bu projenin dosyası yok)
    }

    [Fact]
    public void ComputeCommittedFingerprint_returns_null_when_tracked_blob_map_is_empty_no_commits_repo()
    {
        var emptyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        string? fp = IncrementalPlanner.ComputeCommittedFingerprint(emptyMap, ["L1.cs", "L1.csproj"]);

        Assert.Null(fp);
    }

    [Fact]
    public void ComputeCommittedFingerprint_reordering_project_files_yields_the_same_fingerprint()
    {
        var trackedBlobHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a.cs"] = "shaA",
            ["b.cs"] = "shaB",
        };

        string? fp1 = IncrementalPlanner.ComputeCommittedFingerprint(trackedBlobHashes, ["a.cs", "b.cs"]);
        string? fp2 = IncrementalPlanner.ComputeCommittedFingerprint(trackedBlobHashes, ["b.cs", "a.cs"]);

        Assert.Equal(fp1, fp2);
    }

    [Fact]
    public void ComputeCommittedFingerprint_ignores_non_build_affecting_files_even_when_present_in_the_map()
    {
        var trackedBlobHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a.cs"] = "shaA",
            ["README.md"] = "shaReadme",
        };

        string? fpWithoutReadme = IncrementalPlanner.ComputeCommittedFingerprint(trackedBlobHashes, ["a.cs"]);
        string? fpWithReadme = IncrementalPlanner.ComputeCommittedFingerprint(trackedBlobHashes, ["a.cs", "README.md"]);

        Assert.Equal(fpWithoutReadme, fpWithReadme); // .md build-etkileyen değil -> filtrelenir
    }

    [Fact]
    public void ComputeCommittedFingerprint_changing_a_matched_files_blob_sha_changes_the_fingerprint()
    {
        var mapV1 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a.cs"] = "sha1" };
        var mapV2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a.cs"] = "sha2" };

        string? fp1 = IncrementalPlanner.ComputeCommittedFingerprint(mapV1, ["a.cs"]);
        string? fp2 = IncrementalPlanner.ComputeCommittedFingerprint(mapV2, ["a.cs"]);

        Assert.NotEqual(fp1, fp2);
    }

    [Fact]
    public void ComputeCommittedFingerprint_a_new_uncommitted_project_file_absent_from_the_map_does_not_affect_the_fingerprint()
    {
        // b.cs henüz commit'lenmemiş (map'te yok) — bu YENİ dosyanın varlığı committed fingerprint'i etkilemez;
        // onun sinyali ayrıca working-tree dirty listesi (local-diff terimi, inPlace modda) üzerinden gelir.
        var trackedBlobHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["a.cs"] = "shaA" };

        string? fpOnlyTracked = IncrementalPlanner.ComputeCommittedFingerprint(trackedBlobHashes, ["a.cs"]);
        string? fpWithUncommittedExtra = IncrementalPlanner.ComputeCommittedFingerprint(trackedBlobHashes, ["a.cs", "b.cs"]);

        Assert.Equal(fpOnlyTracked, fpWithUncommittedExtra);
    }

    // ---- [A1/T15] Sıra bağımsızlığı: plan.Nodes TOPOLOJİK OLMAYABİLİR (LayerEngine sert faz bariyeri bir
    // projeyi kendi bağımlılığından ÖNCE koyabilir — warn-only tasarımın kasıtlı sonucu). Propagation buna
    // rağmen doğru olmalı; aksi halde bir dependent "up to date" diye sessizce ATLANIRDI (under-build). ------

    private const string UpId = @"C:\r\Up.csproj";
    private const string DownId = @"C:\r\Down.csproj";
    private const string UpSourcePath = @"C:\r\Up.cs";

    /// <summary>Planın TÜM imzalarını hesaplayıp <paramref name="id"/>'ninkini döner. Up projesi (tek) dirty
    /// dosyasıyla gelir ve o dosyanın içeriği <paramref name="upstreamContent"/>'tir — yani iki çağrı arasında
    /// DEĞİŞEN tek şey upstream'in kaynağıdır.</summary>
    private static string? SignatureOf(BuildPlan plan, string id, string upstreamContent) =>
        IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan,
            headCommit: "headA",
            dirtyFilesForNode: DirtyLookup(Dirty((UpId, [UpSourcePath]))),
            readFileContent: ContentMap((UpSourcePath, upstreamContent)),
            committedFingerprintForNode: FingerprintLookup(Fingerprints((UpId, "fpUp"), (DownId, "fpDown"))),
            state: new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase),
            inPlace: true,
            mode: DependentMode.Safe).SignatureById[id];

    [Fact]
    public void Upstream_change_propagates_even_when_plan_order_is_not_topological()
    {
        // LayerEngine reorder'ı bir projeyi kendi bağımlılığından ÖNCE koyabilir (LayerEngine.cs:77-81).
        // Dependent (Down) dizide Upstream'den ÖNCE geliyor; propagation buna rağmen çalışmalı.
        var up = Node(UpId, buildOrder: 1, inCycle: false);
        var down = Node(DownId, buildOrder: 0, inCycle: false, UpId);
        var plan = new BuildPlan([down, up], [], "Debug");

        string? sigDownBefore = SignatureOf(plan, DownId, upstreamContent: "v1");
        string? sigDownAfter = SignatureOf(plan, DownId, upstreamContent: "v2");

        Assert.NotEqual(sigDownBefore, sigDownAfter);
    }

    // ---- [A3] SCC kompozit imzası: bir SCC (dependency cycle) eskiden "imza kara deliği"ydi — on-stack
    // guard'a çarpan üyenin upstream terimi SABİT NullMarker'a düşüyordu, dolayısıyla SCC İÇİNDEKİ gerçek bir
    // kaynak değişimi SCC DIŞINDAKİ bir downstream'e (üye kendi imzasını o downstream'e besliyor olsa bile)
    // yansımayabiliyordu: sonuç ziyaret SIRASINA bağlıydı ve dependent bir sonraki Build'de sessizce "up to
    // date" sayılıp atlanıyordu (under-build). Artık SCC başına TEK kompozit imza üretilir. -----------------

    private const string CycA = @"C:\r\A.csproj";
    private const string CycB = @"C:\r\B.csproj";
    private const string CycD = @"C:\r\D.csproj";
    private const string CycBSourcePath = @"C:\r\B.cs";

    /// <summary>SCC={A,B} (A→B, B→A) + D: cycle DIŞINDA, A'ya bağımlı. Düğümler ayrı döner ki testler plan
    /// dizisindeki SIRAYI kendileri seçebilsin.</summary>
    private static (ProjectNode A, ProjectNode B, ProjectNode D) CycleNodes() => (
        Node(CycA, 0, inCycle: true, CycB),
        Node(CycB, 1, inCycle: true, CycA),
        Node(CycD, 2, inCycle: false, CycA));

    /// <summary>Planın TÜM imzalarını hesaplayıp <paramref name="id"/>'ninkini döner. DEĞİŞEN tek girdi, SCC
    /// üyesi B'nin dirty kaynak dosyasının İÇERİĞİdir (<paramref name="bContent"/>).</summary>
    private static string? CycleSignatureOf(BuildPlan plan, string id, string bContent) =>
        IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan,
            headCommit: "headA",
            dirtyFilesForNode: DirtyLookup(Dirty((CycB, [CycBSourcePath]))),
            readFileContent: ContentMap((CycBSourcePath, bContent)),
            committedFingerprintForNode: FingerprintLookup(Fingerprints((CycA, "fpA"), (CycB, "fpB"), (CycD, "fpD"))),
            state: new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase),
            inPlace: true,
            mode: DependentMode.Safe).SignatureById[id];

    [Fact]
    public void A_source_change_inside_a_cycle_propagates_to_a_downstream_outside_the_cycle()
    {
        var (a, b, d) = CycleNodes();
        // Sıra KASITLI: B önce ziyaret edilirse A, B'nin içinden on-stack guard'a çarpar ve A'nın imzası B'nin
        // KENDİ terimlerini HİÇ görmez; D de yalnız A'yı okuduğu için B'deki değişim D'ye ULAŞMAZDI.
        var plan = new BuildPlan([b, a, d], [[CycA, CycB]], "Debug");

        string? before = CycleSignatureOf(plan, CycD, bContent: "v1");
        string? after = CycleSignatureOf(plan, CycD, bContent: "v2");

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void The_same_cycle_graph_in_two_different_node_orders_yields_the_same_signature()
    {
        // Asıl invaryant "değişim yayılıyor" değil, "AYNI graf, FARKLI düğüm sırasıyla AYNI imzayı üretir"dir:
        // tüm incremental sistem bu byte-kararlılığına dayanır (state'e persist edilen imza, bir sonraki
        // koşuda YENİDEN hesaplananla karşılaştırılır — arada plan sırası değişmiş olabilir).
        var (a, b, d) = CycleNodes();
        var order1 = new BuildPlan([a, b, d], [[CycA, CycB]], "Debug");
        var order2 = new BuildPlan([b, a, d], [[CycA, CycB]], "Debug");

        Assert.Equal(CycleSignatureOf(order1, CycD, "v1"), CycleSignatureOf(order2, CycD, "v1"));
        Assert.Equal(CycleSignatureOf(order1, CycA, "v1"), CycleSignatureOf(order2, CycA, "v1"));
    }

    [Fact]
    public void Both_members_of_an_scc_resolve_to_the_same_composite_signature()
    {
        var (a, b, d) = CycleNodes();
        var plan = new BuildPlan([a, b, d], [[CycA, CycB]], "Debug");

        Assert.Equal(CycleSignatureOf(plan, CycA, "v1"), CycleSignatureOf(plan, CycB, "v1"));
    }

    [Fact]
    public void A_project_that_depends_on_itself_still_terminates_and_yields_a_signature()
    {
        // Kendine bağımlı düğüm TopoSort'ta cycle SAYILMAZ (Cycles yalnız >1 üyeli SCC'leri taşır, InCycle=false)
        // — sonsuz özyinelemeyi engelleyen TEK şey on-stack guard'dır; bu test onu doğrudan pinler.
        const string selfId = @"C:\r\Self.csproj";
        var self = Node(selfId, 0, inCycle: false, selfId);
        var plan = new BuildPlan([self], [], "Debug");

        var (_, signatures) = IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan, headCommit: "headA", dirtyFilesForNode: DirtyLookup(Dirty()), readFileContent: NoRead,
            committedFingerprintForNode: FingerprintLookup(Fingerprints((selfId, "fpSelf"))),
            state: new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase),
            inPlace: true, mode: DependentMode.Safe);

        Assert.NotNull(signatures[selfId]);
    }
}
