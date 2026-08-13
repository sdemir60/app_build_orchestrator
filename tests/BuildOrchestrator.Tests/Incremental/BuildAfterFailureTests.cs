using BuildOrchestrator.Contracts.Ipc;
using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

namespace BuildOrchestrator.Tests.Incremental;

/// <summary>
/// [design v1.7.0 §2.7-11 · §3.1] Hata sonrası bir sonraki <b>Build</b> ne derler?
///
/// <para><b>DEĞİŞEN KURAL.</b> Eski iddia ayrı bir <c>RunMode.RetryFailed</c> yüzeyine aitti
/// (<c>Scheduling/RetryFailedTests</c>): "retry kümesi = failed projeler + transitif bağımlıları; succeeded ve
/// skipped olanlara dokunulmaz". O mod kaldırıldı — Build zaten AYNI kümeyi derliyor ve bu sınıf bunu pinler.
/// Mekanizma iki parçadır ve ikisi de zaten vardı:</para>
/// <list type="number">
///   <item>Başarısız proje <c>LastResult=Failed</c> ile geçersizleşir (<c>RunCoordinator</c>'ın
///     invalidasyonu) → <c>WillBuildEvaluator</c> onu "up to date" sayamaz.</item>
///   <item>Onun bağımlıları o koşuda yeşil bitse de <b>depIssue taşıdıkları için imzalarını persist ETMEZ</b>
///     ([A2] <c>PersistBuildStateOnSuccess</c> koşulu) → stored imzaları hatadan ÖNCEKİ kaynağa aittir ve
///     bir sonraki Build'de taze imzayla eşleşmez.</item>
/// </list>
///
/// <para>Kurulum deseni <see cref="IncrementalPlannerTests"/> ile aynıdır: gerçek repo yok, git olguları
/// (fingerprint / dirty dosya listesi / state) enjekte edilir [D8].</para>
/// </summary>
public class BuildAfterFailureTests
{
    private static ProjectNode Node(string id, int buildOrder, params string[] dependencies) =>
        new(id, id, id, [], dependencies, buildOrder, null, null, InCycle: false, WillBuild: null);

    private static readonly Func<string, string> NoRead = _ => throw new InvalidOperationException("okunmamalıydı");
    private static readonly Func<ProjectNode, IReadOnlyList<string>> NoDirty = _ => [];

    private static Func<string, string> ContentMap(params (string Path, string Content)[] entries)
    {
        var map = entries.ToDictionary(e => e.Path, e => e.Content, StringComparer.Ordinal);
        return path => map.TryGetValue(path, out var c) ? c : throw new KeyNotFoundException(path);
    }

    private static Func<ProjectNode, string?> Fingerprints(params (string Id, string Fingerprint)[] entries)
    {
        var map = entries.ToDictionary(e => e.Id, e => (string?)e.Fingerprint, StringComparer.OrdinalIgnoreCase);
        return node => map.TryGetValue(node.Id, out var fp) ? fp : null;
    }

    /// <summary>
    /// Gerçek hata senaryosu: F1'in kaynağı değişti, F1 patladı; D1 (F1'e bağımlı) ve D2 (D1'e bağımlı) "hata
    /// derlemeyi öldürmez" (A3) sayesinde o koşuda yeşil bitti — ama depIssue taşıdıkları için imzaları
    /// persist EDİLMEDİ, yani stored imzaları hâlâ hatadan önceki kaynağa ait. S bağımsız ve temiz derlendi.
    /// Sonraki Build tam olarak eski retry kümesini alır: F1 + D1 + D2; S atlanır.
    /// </summary>
    [Fact]
    public void The_next_build_takes_the_failed_project_and_its_transitive_dependents_but_not_an_unrelated_sibling()
    {
        var f1 = Node("F1", 0);
        var d1 = Node("D1", 1, "F1");
        var d2 = Node("D2", 2, "D1");
        var s = Node("S", 3);
        var plan = new BuildPlan([f1, d1, d2, s], [], "Debug");

        var fp = Fingerprints(("F1", "fpF1"), ("D1", "fpD1"), ("D2", "fpD2"), ("S", "fpS"));

        // Hatadan ÖNCEKİ dünya: F1.cs = v1, zincir bu hâle karşı derlenmiş ve persist edilmişti.
        var readV1 = ContentMap(("F1.cs", "v1"));
        string oldF1 = BuildSignature.Compute(f1, "Debug", "fpF1", ["F1.cs"], readV1, _ => null, inPlace: true);
        string oldD1 = BuildSignature.Compute(d1, "Debug", "fpD1", [], NoRead, id => id == "F1" ? oldF1 : null, inPlace: true);
        string oldD2 = BuildSignature.Compute(d2, "Debug", "fpD2", [], NoRead, id => id == "D1" ? oldD1 : null, inPlace: true);
        string sigS = BuildSignature.Compute(s, "Debug", "fpS", [], NoRead, _ => null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            // F1: imzası korunur ama sonuç Failed → geçersiz (RunCoordinator'ın invalidasyonu).
            ["F1"] = new BuildState("F1", oldF1, LastResult: BuildResult.Failed),
            // D1/D2: yeşil bittiler ama depIssue taşıdıkları için YENİ imza persist EDİLMEDİ — stored
            // değerler hâlâ hatadan önceki kaynağa ait.
            ["D1"] = new BuildState("D1", oldD1, LastResult: BuildResult.Succeeded),
            ["D2"] = new BuildState("D2", oldD2, LastResult: BuildResult.Succeeded),
            // S: temiz derlendi, imzası güncel.
            ["S"] = new BuildState("S", sigS, LastResult: BuildResult.Succeeded),
        };

        // Şimdi: F1.cs hâlâ dirty ve v2 içeriğinde (düzeltme yapıldı ya da yapılmadı — fark etmez).
        var readV2 = ContentMap(("F1.cs", "v2"));
        Func<ProjectNode, IReadOnlyList<string>> dirty = node => node.Id == "F1" ? ["F1.cs"] : [];

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", dirty, readV2, fp, state,
            inPlace: true, buildCycles: false, mode: DependentMode.Safe);

        var willBuild = result.Nodes.ToDictionary(n => n.Id, n => n.WillBuild, StringComparer.OrdinalIgnoreCase);
        Assert.True(willBuild["F1"]);
        Assert.True(willBuild["D1"]);
        Assert.True(willBuild["D2"]);
        Assert.False(willBuild["S"]);
    }

    /// <summary>
    /// <b>Eski RetryFailed'den bilinçli FARK.</b> Ortam kaynaklı bir hatada (kaynak DEĞİŞMEDİ; kilitli dosya,
    /// dolu disk, öldürülmüş process) sonraki Build yalnız <b>başarısız projeyi</b> alır — bağımlıları
    /// almaz. Eski mod onları da kuyruğa sokardı; gereksizdi: bağımlılar zaten F1'in AYNI çıktısına karşı
    /// derlenmişti ve girdilerinin hiçbiri değişmedi. Küme daralması bilinçlidir, kayıp değil.
    /// </summary>
    [Fact]
    public void An_environmental_failure_with_unchanged_sources_only_rebuilds_the_failed_project_itself()
    {
        var f1 = Node("F1", 0);
        var d1 = Node("D1", 1, "F1");
        var plan = new BuildPlan([f1, d1], [], "Debug");

        var fp = Fingerprints(("F1", "fpF1"), ("D1", "fpD1"));
        string sigF1 = BuildSignature.Compute(f1, "Debug", "fpF1", [], NoRead, _ => null, inPlace: true);
        string sigD1 = BuildSignature.Compute(d1, "Debug", "fpD1", [], NoRead, id => id == "F1" ? sigF1 : null, inPlace: true);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["F1"] = new BuildState("F1", sigF1, LastResult: BuildResult.Failed),
            ["D1"] = new BuildState("D1", sigD1, LastResult: BuildResult.Succeeded),
        };

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, "headA", NoDirty, NoRead, fp, state,
            inPlace: true, buildCycles: false, mode: DependentMode.Safe);

        var willBuild = result.Nodes.ToDictionary(n => n.Id, n => n.WillBuild, StringComparer.OrdinalIgnoreCase);
        Assert.True(willBuild["F1"]);
        Assert.False(willBuild["D1"]);
    }
}
