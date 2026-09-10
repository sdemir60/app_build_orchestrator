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
///   <item>Onun bağımlıları o koşuda yeşil bitse de kayıtlarına <b>DepIssue notu</b> düşer
///     (<c>PersistBuildStateOnSuccess</c>) → <c>WillBuildEvaluator</c> notu görüp onları yine derleme
///     listesinde tutar.</item>
/// </list>
///
/// <para><b>[DEĞİŞEN KURAL] İkinci maddenin mekanizması değişti.</b> Eskiden bu projeler imzalarını HİÇ
/// persist etmezdi (A2) ve bir sonraki Build'de "bayat imza ≠ taze imza" oldukları için derlenirlerdi.
/// Ölçüldü ki o kural defterin ilerlemesini tamamen durduruyor (bir koşuda 24 hata depIssue'yu 96 projeye
/// yaydı; 74 başarının sıfırı yazıldı). Artık kayıt taze imzayla YAZILIR ve aynı sonucu <b>not</b> üretir.
/// Derlenecek küme değişmedi — bu testin iddiaları aynen geçerli; değişen tek şey <c>state</c> kurulumunun
/// gerçeği yansıtması: bayat imza yerine güncel imza + <c>DepIssue: true</c>.</para>
///
/// <para>Kurulum deseni <see cref="IncrementalPlannerTests"/> ile aynıdır: gerçek repo/disk yok, her projeye
/// opak bir içerik fingerprint'i ve state kaydı enjekte edilir [D8].</para>
/// </summary>
public class BuildAfterFailureTests
{
    private static ProjectNode Node(string id, int buildOrder, params string[] dependencies) =>
        new(id, id, id, [], dependencies, buildOrder, null, null, InCycle: false, WillBuild: null);

    private static Func<ProjectNode, string?> Fingerprints(params (string Id, string Fingerprint)[] entries)
    {
        var map = entries.ToDictionary(e => e.Id, e => (string?)e.Fingerprint, StringComparer.OrdinalIgnoreCase);
        return node => map.TryGetValue(node.Id, out var fp) ? fp : null;
    }

    /// <summary>
    /// Gerçek hata senaryosu: F1'in kaynağı değişti, F1 patladı; D1 (F1'e bağımlı) ve D2 (D1'e bağımlı) "hata
    /// derlemeyi öldürmez" (A3) sayesinde o koşuda yeşil bitti — kayıtlarına TAZE imza + <c>DepIssue</c> notu
    /// yazıldı. S bağımsız ve temiz derlendi. Sonraki Build tam olarak eski retry kümesini alır: F1 + D1 + D2;
    /// S atlanır.
    ///
    /// <para>Testin ayırt ediciliği burada: D1/D2'nin kayıtlı imzaları taze kaynakla <b>EŞLEŞİR</b> — onları
    /// derleme listesinde tutan tek şey nottur. Not okunmasaydı ikisi de pre-skip edilir ve bayat bir
    /// binary'e link'li kalırlardı.</para>
    /// </summary>
    [Fact]
    public void The_next_build_takes_the_failed_project_and_its_transitive_dependents_but_not_an_unrelated_sibling()
    {
        var f1 = Node("F1", 0);
        var d1 = Node("D1", 1, "F1");
        var d2 = Node("D2", 2, "D1");
        var s = Node("S", 3);
        var plan = new BuildPlan([f1, d1, d2, s], [], "Debug");

        var fp = Fingerprints(("F1", "fpF1-v2"), ("D1", "fpD1"), ("D2", "fpD2"), ("S", "fpS"));

        // Hatadan ÖNCEKİ dünya: F1'in kaynağı v1, zincir bu hâle karşı derlenmiş ve persist edilmişti.
        string oldF1 = BuildSignature.Compute(f1, "Debug", "fpF1-v1", _ => null);
        string sigS = BuildSignature.Compute(s, "Debug", "fpS", _ => null);

        // Hatalı koşunun BAŞINDA hesaplanan (v2 tabanlı) imzalar — D1/D2 kayıtlarına bunlar yazıldı.
        string newF1 = BuildSignature.Compute(f1, "Debug", "fpF1-v2", _ => null);
        string newD1 = BuildSignature.Compute(d1, "Debug", "fpD1", id => id == "F1" ? newF1 : null);
        string newD2 = BuildSignature.Compute(d2, "Debug", "fpD2", id => id == "D1" ? newD1 : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            // F1: imzası korunur ama sonuç Failed → geçersiz (RunCoordinator'ın invalidasyonu).
            ["F1"] = new BuildState("F1", oldF1, LastResult: BuildResult.Failed),
            // D1/D2: yeşil bittiler; kayıtları TAZE imzayı taşır ama DepIssue notludur — onları listede
            // tutan şey imza farkı DEĞİL, nottur.
            ["D1"] = new BuildState("D1", newD1, LastResult: BuildResult.Succeeded, DepIssue: true),
            ["D2"] = new BuildState("D2", newD2, LastResult: BuildResult.Succeeded, DepIssue: true),
            // S: temiz derlendi, imzası güncel, notu yok.
            ["S"] = new BuildState("S", sigS, LastResult: BuildResult.Succeeded),
        };
        var result = IncrementalPlanner.ComputeWillBuild(
            plan, fp, state,
            buildCycles: false, mode: DependentMode.Safe);

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
        string sigF1 = BuildSignature.Compute(f1, "Debug", "fpF1", _ => null);
        string sigD1 = BuildSignature.Compute(d1, "Debug", "fpD1", id => id == "F1" ? sigF1 : null);

        var state = new Dictionary<string, BuildState>(StringComparer.OrdinalIgnoreCase)
        {
            ["F1"] = new BuildState("F1", sigF1, LastResult: BuildResult.Failed),
            ["D1"] = new BuildState("D1", sigD1, LastResult: BuildResult.Succeeded),
        };

        var result = IncrementalPlanner.ComputeWillBuild(
            plan, fp, state,
            buildCycles: false, mode: DependentMode.Safe);

        var willBuild = result.Nodes.ToDictionary(n => n.Id, n => n.WillBuild, StringComparer.OrdinalIgnoreCase);
        Assert.True(willBuild["F1"]);
        Assert.False(willBuild["D1"]);
    }
}
