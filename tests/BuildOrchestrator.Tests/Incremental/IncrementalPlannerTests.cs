using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests.Incremental;

// [T25][A6][D1] IncrementalPlanner — GLOBAL graf propagation (Safe = dirty + transitive dependents; Fast =
// yalnız dirty, cascade yok) + skip gate. BuildSignature + BuildStateStore + WillBuildEvaluator/BuildPreview
// seam'ine bağlanır. Testler diske DOKUNMAZ: her düğüme opak bir içerik fingerprint'i doğrudan enjekte edilir
// (o fingerprint'in gerçek dosyalardan nasıl hesaplandığını ProjectInputsTests + IncrementalRunBinderTests
// pinler).
//
// DEĞİŞEN KURAL: eskiden buraya git olguları enjekte ediliyordu — headCommit (hollow kapısı), per-project
// COMMITTED fingerprint (ls-tree blob'ları) ve per-node dirty dosya listesi + içerikleri. D1 ile karar tek bir
// kaynağa, diske indi: git imzaya hiç girmiyor, in-place/worktree ayrımı kalktı ve "anlamlı taban yok" (hollow)
// diye bir hâl kalmadı. Gerekçe: commit'lenmiş xaml/resx değişiklikleri ve git'e girmemiş dosyalar eski
// formülde görünmüyordu (bkz. plan 2026-09-10-01-25-content-based-incremental-plan.md).
public class IncrementalPlannerTests
{
    private static ProjectNode Node(string id, int buildOrder, bool inCycle, params string[] dependencies) =>
        new(id, id, id, [], dependencies, buildOrder, null, null, InCycle: inCycle, WillBuild: null);

    private static readonly Dictionary<string, BuildState> NoState = new(StringComparer.OrdinalIgnoreCase);

    // [D1] Per-project içerik fingerprint'i enjeksiyonu: her projeye ayrı bir opak token atanır. Bir projenin
    // "kendi dosyaları değişti" senaryosu, o token'ın DEĞİŞMESİYLE ifade edilir — gerçek dosya okuması bu
    // katmanın işi değildir (D8 — repo-free testler).
    private static Dictionary<string, string?> Fingerprints(params (string Id, string? Fingerprint)[] entries) =>
        entries.ToDictionary(e => e.Id, e => e.Fingerprint, StringComparer.OrdinalIgnoreCase);

    private static Func<ProjectNode, string?> FingerprintLookup(IReadOnlyDictionary<string, string?> map) =>
        node => map.TryGetValue(node.Id, out var fp) ? fp : null;

    // ---- L1 -> L2 -> L3 chain: kök değişti, Safe TÜM zincire yayılır, Fast yalnız kökte kalır -----------------

    [Fact]
    public void chain_root_content_change_propagates_to_all_transitive_dependents_in_safe_mode()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        // Baseline: L1 önceden v1 içeriğiyle başarıyla derlenmiş; zincir tutarlı imzalarla state'e yazılmış.
        var after = Fingerprints(("L1", "fpL1-v2"), ("L2", "fpL2"), ("L3", "fpL3"));
        string sigL1Old = BuildSignature.Compute(l1, "Debug", "fpL1-v1", _ => null);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2", id => id == "L1" ? sigL1Old : null);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "fpL3", id => id == "L2" ? sigL2Old : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1Old, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3Old, LastResult: BuildResult.Succeeded),
        };

        // Şimdi: L1'in KENDİ bir dosyası değişti (içerik fingerprint'i v2); L2/L3'ün dosyaları aynı.
        var result = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(after), state,
            buildCycles: false, mode: DependentMode.Safe);

        Assert.True(result.Nodes.Single(n => n.Id == "L1").WillBuild);
        Assert.True(result.Nodes.Single(n => n.Id == "L2").WillBuild); // transitive propagation
        Assert.True(result.Nodes.Single(n => n.Id == "L3").WillBuild); // transitive propagation
    }

    [Fact]
    public void chain_root_content_change_does_not_cascade_in_fast_mode_only_root_rebuilds()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        var after = Fingerprints(("L1", "fpL1-v2"), ("L2", "fpL2"), ("L3", "fpL3"));
        string sigL1Old = BuildSignature.Compute(l1, "Debug", "fpL1-v1", _ => null);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2", id => id == "L1" ? sigL1Old : null);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "fpL3", id => id == "L2" ? sigL2Old : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1Old, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3Old, LastResult: BuildResult.Succeeded),
        };

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(after), state,
            buildCycles: false, mode: DependentMode.Fast);

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
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2", _ => null);
        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            // L1 hiç state'e girmemiş (never-built) — kayıt yok.
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
        };


        var safe = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), state,
            buildCycles: false, mode: DependentMode.Safe);
        Assert.True(safe.Nodes.Single(n => n.Id == "L1").WillBuild); // never-built
        Assert.True(safe.Nodes.Single(n => n.Id == "L2").WillBuild); // upstream (L1) imzası artık farklı (gerçek vs null) -> propagate

        var fast = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), state,
            buildCycles: false, mode: DependentMode.Fast);
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
        string sigL1 = BuildSignature.Compute(l1, "Debug", "fpL1", _ => null);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "fpL2", id => id == "L1" ? sigL1 : null);
        string sigL3 = BuildSignature.Compute(l3, "Debug", "fpL3", id => id == "L2" ? sigL2 : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3, LastResult: BuildResult.Succeeded),
        };

        // Aynı state (Debug'da kaydedilmiş), şimdi Release ile aynen aynı (temiz, dirty yok) plan çalıştırılıyor.
        var planRelease = planDebug with { Configuration = "Release" };

        var result = IncrementalPlanner.ComputeWillBuild(
            planRelease, FingerprintLookup(fp), state,
            buildCycles: false, mode: DependentMode.Safe);

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
        string sigL1 = BuildSignature.Compute(l1, "Debug", "fpL1", _ => null);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "fpL2", id => id == "L1" ? sigL1 : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2, LastResult: BuildResult.Succeeded),
        };

        var planRelease = planDebug with { Configuration = "Release" };

        var result = IncrementalPlanner.ComputeWillBuild(
            planRelease, FingerprintLookup(fp), state,
            buildCycles: false, mode: DependentMode.Fast);

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
        string sigL1 = BuildSignature.Compute(l1, "Debug", "fpL1", _ => null);
        string sigL2 = BuildSignature.Compute(l2, "Debug", "fpL2", id => id == "L1" ? sigL1 : null);
        string sigL3 = BuildSignature.Compute(l3, "Debug", "fpL3", id => id == "L2" ? sigL2 : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3, LastResult: BuildResult.Succeeded),
        };


        var result = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), state,
            buildCycles: false, mode: DependentMode.Safe);

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

    // ---- inCycle -> cevabı kill switch belirler [Task 11] -------------------------------------------------

    /// <summary>
    /// [Task 11] Bir SCC üyesinin önizlemesi artık <c>buildCycles</c>'a BAĞLIDIR ve planner bu bayrağı
    /// <see cref="Core.Planning.WillBuildEvaluator"/>'a GERÇEKTEN taşır.
    /// <para><b>Eski iddia (değişti):</b> bu test
    /// <c>cycle_members_never_build_regardless_of_signature_or_state</c> adıyla "cycle üyesi HER ZAMAN
    /// <c>WillBuild=false</c>" diye pinliyordu. O iddia doğruydu çünkü planner <c>buildCycles: false</c>'ı
    /// SABİT geçiyordu; anahtar açıkken motor üyeleri turlarla DERLEDİĞİ için önizleme ile motor ayrışıyordu
    /// (nokta "derlenmeyecek" der, proje derlenirdi). Kural artık: kapalıyken false (öncesiyle birebir),
    /// açıkken sıradan imza/state mantığı.</para>
    /// <para>İki dal AYNI plan ve AYNI girdilerle ölçülür — fark yalnız bayraktır, yani testin ayırt edici
    /// gücü doğrudan <c>buildCycles</c>'ın taşınmasındadır.</para>
    /// </summary>
    [Fact]
    public void cycle_members_follow_the_build_cycles_flag_instead_of_being_hardcoded_to_false()
    {
        var a = Node("A", 0, inCycle: true, "B");
        var b = Node("B", 1, inCycle: true, "A");
        var plan = new BuildPlan([a, b], [["A", "B"]], "Debug");

        var fp = Fingerprints(("A", "fpA"), ("B", "fpB"));
        var neverBuilt = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase);

        var off = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), neverBuilt,
            buildCycles: false, mode: DependentMode.Safe);

        Assert.False(off.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.False(off.Nodes.Single(n => n.Id == "B").WillBuild);

        var on = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), neverBuilt,
            buildCycles: true, mode: DependentMode.Safe);

        // Hiç derlenmemiş ⇒ sıradan bir proje gibi "derlenecek". Motor da (anahtar açıkken) tam olarak bunu
        // yapar: grubu turlarla derler.
        Assert.True(on.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.True(on.Nodes.Single(n => n.Id == "B").WillBuild);
    }

    /// <summary>[Task 11] Anahtar AÇIKKEN bir SCC "güncel" de olabilir: bileşik imza state'teki
    /// <c>BuiltSignature</c> ile birebir ve son sonuç Succeeded ise üyeler <c>WillBuild=false</c> gelir.
    /// Motorun bu durumda grubu pre-skip ettiği <c>CycleRoundsTests</c>'te pinlidir — bu iki testin BİRLİKTE
    /// söylediği şey, önizleme ile motorun anahtarın AÇIK konumunda da uyuştuğudur. Bileşik imza üyeler
    /// arasında ORTAK olduğu için ikisi birden temizdir (imza kaynağı: <see cref="IncrementalPlanner"/>'ın
    /// SCC kompozit hesabı).</summary>
    [Fact]
    public void a_cycle_whose_composite_signature_matches_the_built_state_previews_as_up_to_date()
    {
        var a = Node("A", 0, inCycle: true, "B");
        var b = Node("B", 1, inCycle: true, "A");
        var plan = new BuildPlan([a, b], [["A", "B"]], "Debug");

        var fp = Fingerprints(("A", "fpA"), ("B", "fpB"));

        // Bileşik imzayı planner'ın KENDİSİNDEN al (elle yeniden hesaplamak kompozitin şeklini kopyalardı).
        var signatures = IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan, FingerprintLookup(fp), new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase),
            buildCycles: true, mode: DependentMode.Safe).SignatureById;
        string composite = signatures["A"]!;
        Assert.Equal(composite, signatures["B"]);   // sanity: kompozit üyeler arasında ORTAK

        var built = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new BuildState("A", composite, LastResult: BuildResult.Succeeded),
            ["B"] = new BuildState("B", composite, LastResult: BuildResult.Succeeded),
        };

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), built,
            buildCycles: true, mode: DependentMode.Safe);

        Assert.False(result.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.False(result.Nodes.Single(n => n.Id == "B").WillBuild);
    }

    // ---- Sürüm kontrolü olmayan / commit'i olmayan çalışma alanı da TAM karar üretir ---------------------

    /// <summary>
    /// [D1] Hiçbir dosyası okunamayan bir proje bile bir KARAR alır: "hiç derlenmemiş" ⇒ derlenecek.
    ///
    /// <para><b>Eski iddia (değişti):</b> bu test
    /// <c>null_head_commit_yields_hollow_null_will_build_for_every_project</c> adıyla "headCommit null ise TÜM
    /// düğümler hollow (<c>WillBuild=null</c>)" diye pinliyordu ve fingerprint fonksiyonunun HİÇ çağrılmadığını
    /// da doğruluyordu. O kapı, imzanın git'ten beslendiği dönemin artığıydı: commit'i olmayan bir repoda
    /// "committed fingerprint" diye bir şey yoktu, dolayısıyla karar da verilemiyordu. Karar diskten verildiğinde
    /// bu kısıt ortadan kalkar — git'i bozuk bir makinede, commit'siz bir repoda ya da sürüm kontrolsüz bir
    /// klasörde de doğru cevap üretilir. Kullanıcı için anlamı: Sync artık "project states unknown" demek
    /// yerine gerçek sayıları yazar.</para>
    /// </summary>
    [Fact]
    public void a_project_whose_content_cannot_be_read_is_still_decided_as_never_built()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var plan = new BuildPlan([l1, l2], [], "Debug");

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, _ => null, NoState, buildCycles: false, mode: DependentMode.Safe);

        Assert.True(result.Nodes.Single(n => n.Id == "L1").WillBuild);
        Assert.Equal(WillBuildReason.NeverBuilt, result.Nodes.Single(n => n.Id == "L1").WillBuildReason);
        Assert.True(result.Nodes.Single(n => n.Id == "L2").WillBuild);
    }

    // ---- Diamond: A -> {B, C} -> D; ortak upstream (A) değişti -> her iki kol + join Safe'te yayılır -------

    [Fact]
    public void diamond_shared_upstream_change_propagates_through_both_branches_and_the_join_in_safe_mode()
    {
        var a = Node("A", 0, inCycle: false);
        var b = Node("B", 1, inCycle: false, "A");
        var c = Node("C", 2, inCycle: false, "A");
        var d = Node("D", 3, inCycle: false, "B", "C");
        var plan = new BuildPlan([a, b, c, d], [], "Debug");

        var after = Fingerprints(("A", "fpA-v2"), ("B", "fpB"), ("C", "fpC"), ("D", "fpD"));
        string sigAOld = BuildSignature.Compute(a, "Debug", "fpA-v1", _ => null);
        string sigBOld = BuildSignature.Compute(b, "Debug", "fpB", id => id == "A" ? sigAOld : null);
        string sigCOld = BuildSignature.Compute(c, "Debug", "fpC", id => id == "A" ? sigAOld : null);
        string sigDOld = BuildSignature.Compute(d, "Debug", "fpD", id => id == "B" ? sigBOld : id == "C" ? sigCOld : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["A"] = new BuildState("A", sigAOld, LastResult: BuildResult.Succeeded),
            ["B"] = new BuildState("B", sigBOld, LastResult: BuildResult.Succeeded),
            ["C"] = new BuildState("C", sigCOld, LastResult: BuildResult.Succeeded),
            ["D"] = new BuildState("D", sigDOld, LastResult: BuildResult.Succeeded),
        };

        // A'nın kendi dosyaları değişti; B/C/D'ninki aynı.
        var safe = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(after), state,
            buildCycles: false, mode: DependentMode.Safe);

        Assert.True(safe.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "B").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "C").WillBuild);
        Assert.True(safe.Nodes.Single(n => n.Id == "D").WillBuild); // join, iki koldan da propagate

        var fast = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(after), state,
            buildCycles: false, mode: DependentMode.Fast);

        Assert.True(fast.Nodes.Single(n => n.Id == "A").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "B").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "C").WillBuild);
        Assert.False(fast.Nodes.Single(n => n.Id == "D").WillBuild);
    }

    // ---- GRANÜLERLİK: bir değişiklik yalnız BİR projenin dosyalarına dokunuyorsa, yalnız O proje (+ Safe'te
    // transitive dependent'leri) dirty olur — ilişkisiz projeler DOKUNULMAZ. Bu, imzanın repo-GLOBAL bir HEAD
    // commit'i taşıdığı ilk tasarımda imkânsızdı: tek bir yeni commit HER düğümün "own" terimini değiştirdiği
    // için ilişkisiz projeler de dirty işaretleniyordu (over-build). ---------------------------------------

    [Fact]
    public void a_change_in_a_leaf_project_does_not_dirty_unrelated_upstream_projects()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        // Baseline: L1/L2/L3, hepsi önceden clean + Succeeded olarak state'e yazılmış (kendi eski committed
        // fingerprint'leriyle tutarlı imzalar).
        string sigL1Old = BuildSignature.Compute(l1, "Debug", "fpL1-old", _ => null);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2-old", id => id == "L1" ? sigL1Old : null);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "fpL3-old", id => id == "L2" ? sigL2Old : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1Old, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3Old, LastResult: BuildResult.Succeeded),
        };

        // Şimdi: değişiklik YALNIZ L3'ün kendi dosyalarına dokundu — L3'ün içerik fingerprint'i farklı,
        // L1/L2'ninki AYNEN eskisi gibi.
        var fp = Fingerprints(("L1", "fpL1-old"), ("L2", "fpL2-old"), ("L3", "fpL3-NEW"));

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), state,
            buildCycles: false, mode: DependentMode.Safe);

        Assert.False(result.Nodes.Single(n => n.Id == "L1").WillBuild); // ilişkisiz — commit onu ETKİLEMEDİ
        Assert.False(result.Nodes.Single(n => n.Id == "L2").WillBuild); // ilişkisiz — commit onu ETKİLEMEDİ
        Assert.True(result.Nodes.Single(n => n.Id == "L3").WillBuild);  // yalnız BU projenin dosyası değişti
    }

    [Fact]
    public void a_change_in_the_root_project_propagates_in_safe_but_not_in_fast()
    {
        var l1 = Node("L1", 0, inCycle: false);
        var l2 = Node("L2", 1, inCycle: false, "L1");
        var l3 = Node("L3", 2, inCycle: false, "L2");
        var plan = new BuildPlan([l1, l2, l3], [], "Debug");

        string sigL1Old = BuildSignature.Compute(l1, "Debug", "fpL1-old", _ => null);
        string sigL2Old = BuildSignature.Compute(l2, "Debug", "fpL2-old", id => id == "L1" ? sigL1Old : null);
        string sigL3Old = BuildSignature.Compute(l3, "Debug", "fpL3-old", id => id == "L2" ? sigL2Old : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["L1"] = new BuildState("L1", sigL1Old, LastResult: BuildResult.Succeeded),
            ["L2"] = new BuildState("L2", sigL2Old, LastResult: BuildResult.Succeeded),
            ["L3"] = new BuildState("L3", sigL3Old, LastResult: BuildResult.Succeeded),
        };

        // Değişiklik YALNIZ L1'in (kökün) dosyalarına dokundu — L2/L3'ünki aynı.
        var fp = Fingerprints(("L1", "fpL1-NEW"), ("L2", "fpL2-old"), ("L3", "fpL3-old"));

        var safe = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), state,
            buildCycles: false, mode: DependentMode.Safe);

        Assert.True(safe.Nodes.Single(n => n.Id == "L1").WillBuild); // kendi dosyası değişti
        Assert.True(safe.Nodes.Single(n => n.Id == "L2").WillBuild); // transitive propagation
        Assert.True(safe.Nodes.Single(n => n.Id == "L3").WillBuild); // transitive propagation

        var fast = IncrementalPlanner.ComputeWillBuild(
            plan, FingerprintLookup(fp), state,
            buildCycles: false, mode: DependentMode.Fast);

        Assert.True(fast.Nodes.Single(n => n.Id == "L1").WillBuild);  // kendi dosyası değişti
        Assert.False(fast.Nodes.Single(n => n.Id == "L2").WillBuild); // frozen upstream -> cascade yok
        Assert.False(fast.Nodes.Single(n => n.Id == "L3").WillBuild); // frozen upstream -> cascade yok
    }

    // ---- ComputeContentFingerprint: girdi dosyaları + içerik özetleri -> tek deterministik hash ----------
    //
    // DEĞİŞEN KURAL: burada eskiden ComputeCommittedFingerprint vardı ve git'in tracked-blob haritası ile
    // projenin dosyalarının KESİŞİMİNİ hash'liyordu. O fonksiyon D1 ile kaldırıldı: haritada olmayan (git'e
    // eklenmemiş, gitignore'lanmış) bir dosya imzaya hiç girmiyordu ve harici kökler zaten o ağaçta yoktu.
    // Yeni fonksiyon aynı şekli (sıra bağımsız, ayraç-güvenli, boş küme -> null) diskten okunan içerikle korur.

    private static IReadOnlyList<ProjectInput> Inputs(params string[] paths) =>
        [.. paths.Select(p => new ProjectInput(p, p))];

    private static Func<string, string> Term(string root) => path => IncrementalRunBinder.PathTerm(root, path);

    private static Func<string, string?> Hashes(params (string Path, string? Hash)[] entries)
    {
        var map = entries.ToDictionary(e => e.Path, e => e.Hash, StringComparer.OrdinalIgnoreCase);
        return path => map.TryGetValue(path, out var h) ? h : null;
    }

    [Fact]
    public void ComputeContentFingerprint_returns_null_when_nothing_could_be_read()
    {
        // Klasörü silinmiş / tamamen kilitli bir proje: hiçbir terim üretilemez. null, "hiç derlenmemiş gibi
        // davran" demektir (over-build) — sessizce "güncel" demekten YEĞDİR.
        string? fp = IncrementalPlanner.ComputeContentFingerprint(
            Inputs(@"C:\r\A.cs"), Term(@"C:\r"), Hashes((@"C:\r\A.cs", null)));

        Assert.Null(fp);
    }

    [Fact]
    public void ComputeContentFingerprint_returns_null_for_an_empty_input_set()
        => Assert.Null(IncrementalPlanner.ComputeContentFingerprint([], Term(@"C:\r"), Hashes()));

    [Fact]
    public void ComputeContentFingerprint_reordering_the_inputs_yields_the_same_fingerprint()
    {
        var hashes = Hashes((@"C:\r\a.cs", "hashA"), (@"C:\r\b.cs", "hashB"));

        Assert.Equal(
            IncrementalPlanner.ComputeContentFingerprint(Inputs(@"C:\r\a.cs", @"C:\r\b.cs"), Term(@"C:\r"), hashes),
            IncrementalPlanner.ComputeContentFingerprint(Inputs(@"C:\r\b.cs", @"C:\r\a.cs"), Term(@"C:\r"), hashes));
    }

    [Fact]
    public void ComputeContentFingerprint_changing_one_files_content_changes_the_fingerprint()
    {
        var inputs = Inputs(@"C:\r\a.cs", @"C:\r\b.cs");

        Assert.NotEqual(
            IncrementalPlanner.ComputeContentFingerprint(inputs, Term(@"C:\r"),
                Hashes((@"C:\r\a.cs", "hashA"), (@"C:\r\b.cs", "hashB"))),
            IncrementalPlanner.ComputeContentFingerprint(inputs, Term(@"C:\r"),
                Hashes((@"C:\r\a.cs", "hashA"), (@"C:\r\b.cs", "hashB-CHANGED"))));
    }

    [Fact]
    public void ComputeContentFingerprint_a_file_that_disappeared_is_dropped_not_fatal()
    {
        // Canlı build ↔ tarama yarışı: okunamayan dosya yalnız KENDİ terimini kaybeder, fingerprint düşmez.
        string? withBoth = IncrementalPlanner.ComputeContentFingerprint(
            Inputs(@"C:\r\a.cs", @"C:\r\b.cs"), Term(@"C:\r"),
            Hashes((@"C:\r\a.cs", "hashA"), (@"C:\r\b.cs", "hashB")));
        string? withOne = IncrementalPlanner.ComputeContentFingerprint(
            Inputs(@"C:\r\a.cs", @"C:\r\b.cs"), Term(@"C:\r"),
            Hashes((@"C:\r\a.cs", "hashA"), (@"C:\r\b.cs", null)));

        Assert.NotNull(withOne);
        Assert.NotEqual(withBoth, withOne);   // eksik terim FARK EDİLİR (proje yeniden derlenir)
    }

    [Fact]
    public void ComputeContentFingerprint_paths_containing_separator_characters_do_not_collide()
    {
        // Boundary-shift: yol terimi ayraç yanına RAW gömülseydi, içinde ayraç/'=' geçen bir yol iki farklı
        // terim kümesini aynı pre-hash string'ine indirgeyebilirdi. Terim de hash'lendiği için indirgenemez.
        char itemSep = (char)0x1E;
        string weird = @"C:\r\a" + itemSep + "b=c.cs";

        Assert.NotEqual(
            IncrementalPlanner.ComputeContentFingerprint(Inputs(weird), Term(@"C:\r"), Hashes((weird, "hash"))),
            IncrementalPlanner.ComputeContentFingerprint(Inputs(@"C:\r\abc.cs"), Term(@"C:\r"), Hashes((@"C:\r\abc.cs", "hash"))));
    }

    [Fact]
    public void ComputeContentFingerprint_is_root_relative_so_the_same_tree_in_another_root_matches()
    {
        // [D5] Aynı içerik, başka bir kök (worktree ya da ikinci bir klon) → AYNI fingerprint.
        Assert.Equal(
            IncrementalPlanner.ComputeContentFingerprint(
                Inputs(@"C:\main\src\A\A.cs"), Term(@"C:\main"), Hashes((@"C:\main\src\A\A.cs", "hashA"))),
            IncrementalPlanner.ComputeContentFingerprint(
                Inputs(@"D:\pool\wt1\src\A\A.cs"), Term(@"D:\pool\wt1"), Hashes((@"D:\pool\wt1\src\A\A.cs", "hashA"))));
    }

    // ---- [A1/T15] Sıra bağımsızlığı: plan.Nodes TOPOLOJİK OLMAYABİLİR (LayerEngine sert faz bariyeri bir
    // projeyi kendi bağımlılığından ÖNCE koyabilir — warn-only tasarımın kasıtlı sonucu). Propagation buna
    // rağmen doğru olmalı; aksi halde bir dependent "up to date" diye sessizce ATLANIRDI (under-build). ------

    private const string UpId = @"C:\r\Up.csproj";
    private const string DownId = @"C:\r\Down.csproj";

    /// <summary>Planın TÜM imzalarını hesaplayıp <paramref name="id"/>'ninkini döner. İki çağrı arasında
    /// DEĞİŞEN tek şey upstream'in (Up) içerik fingerprint'idir.</summary>
    private static string? SignatureOf(BuildPlan plan, string id, string upstreamFingerprint) =>
        IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan, FingerprintLookup(Fingerprints((UpId, upstreamFingerprint), (DownId, "fpDown"))), NoState,
            buildCycles: false, mode: DependentMode.Safe).SignatureById[id];

    [Fact]
    public void Upstream_change_propagates_even_when_plan_order_is_not_topological()
    {
        // LayerEngine reorder'ı bir projeyi kendi bağımlılığından ÖNCE koyabilir (LayerEngine.cs:77-81).
        // Dependent (Down) dizide Upstream'den ÖNCE geliyor; propagation buna rağmen çalışmalı.
        var up = Node(UpId, buildOrder: 1, inCycle: false);
        var down = Node(DownId, buildOrder: 0, inCycle: false, UpId);
        var plan = new BuildPlan([down, up], [], "Debug");

        string? sigDownBefore = SignatureOf(plan, DownId, upstreamFingerprint: "fpUp-v1");
        string? sigDownAfter = SignatureOf(plan, DownId, upstreamFingerprint: "fpUp-v2");

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
    private const string CycU = @"C:\r\U.csproj"; // SCC'nin DIŞINDAKİ upstream (bkz. ..._upstream_of_a_cycle_... testi)


    /// <summary>SCC={A,B} (A→B, B→A) + D: cycle DIŞINDA, A'ya bağımlı. Düğümler ayrı döner ki testler plan
    /// dizisindeki SIRAYI kendileri seçebilsin.</summary>
    private static (ProjectNode A, ProjectNode B, ProjectNode D) CycleNodes() => (
        Node(CycA, 0, inCycle: true, CycB),
        Node(CycB, 1, inCycle: true, CycA),
        Node(CycD, 2, inCycle: false, CycA));

    /// <summary>Planın TÜM imzalarını hesaplayıp <paramref name="id"/>'ninkini döner. DEĞİŞEN tek girdi, SCC
    /// üyesi B'nin içerik fingerprint'idir (<paramref name="bFingerprint"/>).</summary>
    private static string? CycleSignatureOf(BuildPlan plan, string id, string bFingerprint) =>
        IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan, FingerprintLookup(Fingerprints((CycA, "fpA"), (CycB, bFingerprint), (CycD, "fpD"))), NoState,
            buildCycles: false, mode: DependentMode.Safe).SignatureById[id];

    [Fact]
    public void A_source_change_inside_a_cycle_propagates_to_a_downstream_outside_the_cycle()
    {
        var (a, b, d) = CycleNodes();
        // Sıra KASITLI: B önce ziyaret edilirse A, B'nin içinden on-stack guard'a çarpar ve A'nın imzası B'nin
        // KENDİ terimlerini HİÇ görmez; D de yalnız A'yı okuduğu için B'deki değişim D'ye ULAŞMAZDI.
        var plan = new BuildPlan([b, a, d], [[CycA, CycB]], "Debug");

        string? before = CycleSignatureOf(plan, CycD, bFingerprint: "fpB-v1");
        string? after = CycleSignatureOf(plan, CycD, bFingerprint: "fpB-v2");

        Assert.NotEqual(before, after);
    }

    [Fact]
    public void The_same_cycle_graph_in_two_different_node_orders_yields_the_same_signature()
    {
        // Asıl invaryant "değişim yayılıyor" değil, "AYNI graf, FARKLI düğüm sırasıyla AYNI imzayı üretir"dir:
        // tüm incremental sistem bu byte-kararlılığına dayanır (state'e persist edilen imza, bir sonraki
        // koşuda YENİDEN hesaplananla karşılaştırılır — arada plan sırası değişmiş olabilir).
        var (a, b, d) = CycleNodes();
        // order2'de İKİ sıra da değişir: hem plan.Nodes dizisi hem de Cycles içindeki ÜYE sırası. İkincisi
        // IncrementalPlanner'daki OrderBy'ı (componentOf kurulumu) pinler — yalnız düğüm sırasını değiştirmek
        // o sıralamayı test etmez, çünkü kompozit üyeleri her iki planda da aynı sırada gelirdi.
        // order3, order1'den YALNIZ üye sırasıyla, order2'den ise YALNIZ düğüm sırasıyla ayrılır: böylece bir
        // regresyonda hangi invaryantın kırıldığı kırmızı satırdan OKUNUR (order1↔order2 tek başına ikisini
        // ayırt edemezdi).
        var order1 = new BuildPlan([a, b, d], [[CycA, CycB]], "Debug");
        var order2 = new BuildPlan([b, a, d], [[CycB, CycA]], "Debug");
        var order3 = new BuildPlan([a, b, d], [[CycB, CycA]], "Debug");

        Assert.Equal(CycleSignatureOf(order1, CycD, "fpB-v1"), CycleSignatureOf(order2, CycD, "fpB-v1"));
        Assert.Equal(CycleSignatureOf(order1, CycA, "fpB-v1"), CycleSignatureOf(order2, CycA, "fpB-v1"));
        // YALNIZ üye sırası değişti (düğüm sırası order1 ile aynı) → componentOf'un OrderBy'ı.
        Assert.Equal(CycleSignatureOf(order1, CycD, "fpB-v1"), CycleSignatureOf(order3, CycD, "fpB-v1"));
        Assert.Equal(CycleSignatureOf(order1, CycA, "fpB-v1"), CycleSignatureOf(order3, CycA, "fpB-v1"));
        // YALNIZ düğüm sırası değişti (üye sırası order2 ile aynı) → DFS ziyaret sırasından bağımsızlık.
        Assert.Equal(CycleSignatureOf(order2, CycD, "fpB-v1"), CycleSignatureOf(order3, CycD, "fpB-v1"));
        Assert.Equal(CycleSignatureOf(order2, CycA, "fpB-v1"), CycleSignatureOf(order3, CycA, "fpB-v1"));
    }

    [Fact]
    public void Both_members_of_an_scc_resolve_to_the_same_composite_signature()
    {
        var (a, b, d) = CycleNodes();
        var plan = new BuildPlan([a, b, d], [[CycA, CycB]], "Debug");

        Assert.Equal(CycleSignatureOf(plan, CycA, "fpB-v1"), CycleSignatureOf(plan, CycB, "fpB-v1"));
    }

    [Fact]
    public void A_project_that_depends_on_itself_still_terminates_and_yields_a_signature()
    {
        // Kendine bağımlı düğüm TopoSort'ta cycle SAYILMAZ (Cycles yalnız >1 üyeli SCC'leri taşır, InCycle=false)
        // — sonsuz özyinelemeyi engelleyen TEK şey on-stack guard'dır; bu test onu doğrudan pinler.
        const string selfId = @"C:\r\Self.csproj";
        var self = Node(selfId, 0, inCycle: false, selfId);
        var plan = new BuildPlan([self], [], "Debug");

        string? SigOfSelf() => IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan, FingerprintLookup(Fingerprints((selfId, "fpSelf"))), NoState,
            buildCycles: false, mode: DependentMode.Safe).SignatureById[selfId];

        Assert.NotNull(SigOfSelf());
        // Sonlanma ASIL nokta; ama guard'a çarpan kenarın SABİT bir işarete düşmesi (ziyaret sırasına/geçici
        // duruma bağlı bir değere DEĞİL) imzanın KARARLILIĞI demektir — state'e persist edilen değer bir
        // sonraki koşuda yeniden hesaplananla karşılaştırıldığı için bu, sonlanma kadar bağlayıcıdır.
        Assert.Equal(SigOfSelf(), SigOfSelf());
    }

    [Fact]
    public void A_source_change_upstream_of_a_cycle_propagates_through_the_composite_to_a_downstream_outside_it()
    {
        // U (SCC DIŞI) → SCC={A,B} → D (SCC DIŞI). Kompozitin SCC-DIŞI upstream dalı (IncrementalPlanner:
        // `membersSet.Contains(depId) ? NullMarker : Upstream(depId)` ifadesinin FALSE kolu) YALNIZ burada
        // koşar: diğer [A3] testlerinde SCC üyeleri sadece birbirine bağımlı olduğu için her kenar TRUE koluna
        // düşüyordu. Acceptance'taki "TAM cascade" eşitliği tam da bu mekanizmaya dayanır — değişim SCC'nin
        // İÇİNE girip kompozit üzerinden DIŞARI çıkabilmelidir.
        var u = Node(CycU, 0, inCycle: false);
        var a = Node(CycA, 1, inCycle: true, CycB, CycU);
        var b = Node(CycB, 2, inCycle: true, CycA);
        var d = Node(CycD, 3, inCycle: false, CycA);
        // Sıra KASITLI olarak topolojik DEĞİL (D ve B, A'dan önce) — cascade sıraya bağlı olmamalı.
        var plan = new BuildPlan([d, b, a, u], [[CycA, CycB]], "Debug");

        string? SigOfD(string uFingerprint) => IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan, FingerprintLookup(Fingerprints((CycU, uFingerprint), (CycA, "fpA"), (CycB, "fpB"), (CycD, "fpD"))),
            NoState, buildCycles: false, mode: DependentMode.Safe).SignatureById[CycD];

        Assert.NotEqual(SigOfD("fpU-v1"), SigOfD("fpU-v2"));
    }

    private const string SccE = @"C:\r\E.csproj";      // SCC2 üyeleri (bkz. AdjacentSccNodes)
    private const string SccF = @"C:\r\F.csproj";

    /// <summary>SCC1={A,B} ve SCC2={E,F} ayrık; A (SCC1 üyesi) → E (SCC2 üyesi), yani bir SCC'nin SCC-DIŞI
    /// upstream'i BAŞKA bir SCC'dir. D cycle DIŞINDA ve A'ya bağımlıdır (downstream).</summary>
    private static (ProjectNode A, ProjectNode B, ProjectNode E, ProjectNode F, ProjectNode D) AdjacentSccNodes() => (
        Node(CycA, 0, inCycle: true, CycB, SccE),
        Node(CycB, 1, inCycle: true, CycA),
        Node(SccE, 2, inCycle: true, SccF),
        Node(SccF, 3, inCycle: true, SccE),
        Node(CycD, 4, inCycle: false, CycA));

    /// <summary><see cref="AdjacentSccNodes"/> grafının TÜM imzaları. DEĞİŞEN tek girdi, SCC2 üyesi F'nin
    /// içerik fingerprint'idir (<paramref name="fFingerprint"/>).</summary>
    private static IReadOnlyDictionary<string, string> AdjacentSccSignatures(BuildPlan plan, string fFingerprint) =>
        IncrementalPlanner.ComputeWillBuildWithSignatures(
            plan,
            FingerprintLookup(Fingerprints((CycA, "fpA"), (CycB, "fpB"), (SccE, "fpE"), (SccF, fFingerprint), (CycD, "fpD"))),
            NoState, buildCycles: false, mode: DependentMode.Safe).SignatureById;

    [Fact]
    public void Two_adjacent_sccs_where_one_depends_on_the_other_terminate_and_yield_stable_signatures()
    {
        // SCC1={A,B}, SCC2={E,F}, ayrık; A (SCC1 üyesi) E'ye (SCC2 üyesi) bağımlı → bir SCC'nin SCC-DIŞI
        // upstream'i BAŞKA bir SCC'dir. ComputeComponent bu durumda kendini component seviyesinde çağırır;
        // sonlanmasının gerekçesi condensation'ın DAG olmasıdır. Bu gerekçe bugüne kadar yalnız YORUMDA
        // vardı: bir regresyon burada KIRMIZI assertion olarak değil, ASILI test host'u / StackOverflow
        // olarak patlar — en kötü başarısızlık kipi. Bu test o senaryoyu koşturur.
        var (a, b, e, f, d) = AdjacentSccNodes();
        var plan = new BuildPlan([d, b, a, f, e], [[CycA, CycB], [SccE, SccF]], "Debug");

        var first = AdjacentSccSignatures(plan, "fpF-v1"); // buraya dönülmesi ZATEN sonlanma kanıtıdır
        Assert.All(new[] { CycA, CycB, SccE, SccF, CycD }, id => Assert.NotNull(first[id]));
        Assert.Equal(first[CycA], first[CycB]);   // SCC1 tek kompozit
        Assert.Equal(first[SccE], first[SccF]);   // SCC2 tek kompozit
        Assert.NotEqual(first[CycA], first[SccE]); // AYRI component'ler AYRI kompozit (birleşmiş/çakışmış değil)

        var second = AdjacentSccSignatures(plan, "fpF-v1"); // kararlılık: aynı girdi → aynı imza
        Assert.Equal(first[CycD], second[CycD]);
        Assert.Equal(first[CycA], second[CycA]);
        Assert.Equal(first[SccE], second[SccE]);
    }

    [Fact]
    public void A_source_change_inside_an_upstream_scc_propagates_through_both_composites_to_a_downstream()
    {
        // SCC2={E,F} → SCC1={A,B} → D. Değişim SCC2'nin İÇİNDE (F'nin kaynağında) doğar ve İKİ kompozit
        // zincirinden geçerek cycle DIŞINDAKİ D'ye ulaşmalıdır. Komşu-SCC testi tüm düğümleri TEMİZ tuttuğu
        // için yalnız SONLANMAYI ve kararlılığı pinliyordu: SCC→SCC YAYILIMI — bir SCC'nin kompozitinin, kendi
        // SCC-dışı upstream'i olan BAŞKA bir SCC'nin kompozitini gerçekten İÇERMESİ — hiçbir yerde koşulmuyordu
        // (tek üyeli/iki üyeli tek SCC testleri bu dalı zincirlemiyor). Acceptance'taki "TAM cascade" eşitliği
        // gerçek OSYS grafında tam olarak bu zincire dayanır.
        var (a, b, e, f, d) = AdjacentSccNodes();
        // Sıra KASITLI olarak topolojik DEĞİL (D ve B, A'dan; F, E'den önce) — cascade sıraya bağlı olmamalı.
        var plan = new BuildPlan([d, b, a, f, e], [[CycA, CycB], [SccE, SccF]], "Debug");

        var before = AdjacentSccSignatures(plan, "fpF-v1");
        var after = AdjacentSccSignatures(plan, "fpF-v2");

        Assert.NotEqual(before[SccF], after[SccF]);  // SCC2 kompoziti kendi üyesinin dirty kaynağını görüyor
        Assert.NotEqual(before[SccE], after[SccE]);  // ... ve component'in TÜM üyelerine yazılıyor
        Assert.NotEqual(before[CycA], after[CycA]);  // SCC1 kompoziti SCC-DIŞI upstream'i (SCC2) üzerinden değişti
        Assert.NotEqual(before[CycD], after[CycD]);  // cycle DIŞINDAKİ downstream'e ULAŞTI (under-build yok)
    }
}
