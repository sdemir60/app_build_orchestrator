using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.Planning;

// [T53][A6][v7Δ-8] WillBuildEvaluator karar tablosu + BuildPreview.ComputeWillBuild mapping testleri.
// It-1 kapsamı: currentSignature enjekte edilen bir provider (gerçek imza motoru T25, It-3'te bağlanır).
public class WillBuildTests
{
    [Fact]
    public void hollow_when_signature_null()
        => Assert.Null(WillBuildEvaluator.Evaluate(false, null, null, buildCycles: false));

    [Fact]
    public void true_when_never_built()
        => Assert.True(WillBuildEvaluator.Evaluate(false, "sig1", null, buildCycles: false));

    [Fact]
    public void true_when_dirty()
        => Assert.True(WillBuildEvaluator.Evaluate(false,
            "sig2", new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded), buildCycles: false));

    [Fact]
    public void false_when_up_to_date_after_success() // succeeded → clean
        => Assert.False(WillBuildEvaluator.Evaluate(false,
            "sig1", new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded), buildCycles: false));

    // [DEĞİŞEN KURAL] ESKİ İDDİA: "inCycle olan proje ASLA derlenmez → Evaluate her zaman false".
    // Bu kural kaldırıldı: graf kenarlarının primeri HintPath'tir (ProjectReference değil) ve MSBuild bir
    // HintPath döngüsünü reddetmez — döngü sıralı turlarla derlenebilir. Artık cycle üyeleri de normal
    // dirty/clean kararını alır; ESKİ davranış yalnız kill switch KAPALIYKEN geçerlidir.
    [Fact]
    public void cycle_member_is_not_built_when_switch_is_off()
    {
        Assert.False(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: null, buildCycles: false));
    }

    [Fact]
    public void cycle_member_follows_normal_decision_when_switch_is_on()
    {
        // Hiç derlenmemiş (BuiltSignature yok) ⇒ dirty ⇒ true
        Assert.True(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: null, buildCycles: true));

        // İmza eşleşiyor + son sonuç Succeeded ⇒ güncel ⇒ false
        var clean = new BuildState("p", "sig", LastResult: BuildResult.Succeeded);
        Assert.False(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: "sig", state: clean, buildCycles: true));
    }

    // Anahtar AÇIK olsa bile imza yoksa hollow kalır — cycle bunu ezmez.
    [Fact]
    public void cycle_member_stays_hollow_without_signature()
    {
        Assert.Null(WillBuildEvaluator.Evaluate(
            inCycle: true, currentSignature: null, state: null, buildCycles: true));
    }

    [Fact]
    public void true_when_last_result_failed_even_if_signature_matches()
        => Assert.True(WillBuildEvaluator.Evaluate(false,
            "sig1", new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Failed), buildCycles: false));

    [Fact]
    public void false_when_in_cycle_even_if_signature_null()
        => Assert.False(WillBuildEvaluator.Evaluate(true, null, null, buildCycles: false));

    /// <summary>
    /// Bağımlılığı BAŞARISIZ olmuş bir başarı, bayat bir çıktıya link'lidir: kendi kaynağı değişmese bile
    /// bağımlılık düzelene kadar YENİDEN DERLENİR.
    ///
    /// <para>Bu kural eskiden koordinatörde, "böyle bir başarıyı deftere hiç yazma" biçiminde duruyordu —
    /// ama o, defterin ilerlemesini tamamen durduruyordu (ölçüldü: 24 hatalı projenin depIssue'su 96 projeye
    /// yayıldığı bir koşuda 74 başarının 0'ı yazıldı). Kayıt artık yazılıyor, güvenlik ise BURAYA taşındı:
    /// kaydın <see cref="BuildState.DepIssue"/> notu varsa proje derleme listesinde kalır.</para>
    /// </summary>
    [Fact]
    public void true_when_the_last_success_was_built_against_a_failed_dependency()
        => Assert.True(WillBuildEvaluator.Evaluate(false, "sig1",
            new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded, DepIssue: true),
            buildCycles: false));

    /// <summary>Not TEMİZ bir kayıtta yoktur — aynı imza güncel demektir (kontrol grubu).</summary>
    [Fact]
    public void false_when_the_last_success_carried_no_dependency_issue()
        => Assert.False(WillBuildEvaluator.Evaluate(false, "sig1",
            new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded, DepIssue: false),
            buildCycles: false));

    [Fact]
    public void true_when_signature_matches_but_last_result_null()
        => Assert.True(WillBuildEvaluator.Evaluate(false,
            "sig1", new BuildState("A", BuiltSignature: "sig1", LastResult: null), buildCycles: false));

    // ---- Gerekçe (WillBuildReason) --------------------------------------------------------
    // Karar TEK gövdededir (EvaluateWithReason); Evaluate ona delege eder. Gerekçe kullanıcıya
    // gösterilir: "amber nokta ama commit aynı" görüntüsünün açıklaması buradan gelir.

    private static WillBuildReason? ReasonOf(string? signature, BuildState? state, bool inCycle = false) =>
        WillBuildEvaluator.EvaluateWithReason(inCycle, signature, state, buildCycles: false).Reason;

    [Fact]
    public void reason_is_never_built_when_there_is_no_record()
        => Assert.Equal(WillBuildReason.NeverBuilt, ReasonOf("sig1", null));

    [Fact]
    public void reason_is_last_failed_when_the_previous_run_did_not_succeed()
        => Assert.Equal(WillBuildReason.LastFailed,
            ReasonOf("sig1", new BuildState("A", "sig1", LastResult: BuildResult.Failed)));

    [Fact]
    public void reason_is_dep_issue_when_the_last_success_was_built_against_a_failed_dependency()
        => Assert.Equal(WillBuildReason.DepIssue,
            ReasonOf("sig1", new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true)));

    [Fact]
    public void reason_is_signature_changed_when_the_source_moved()
        => Assert.Equal(WillBuildReason.SignatureChanged,
            ReasonOf("sig2", new BuildState("A", "sig1", LastResult: BuildResult.Succeeded)));

    [Fact]
    public void reason_is_up_to_date_when_nothing_moved()
        => Assert.Equal(WillBuildReason.UpToDate,
            ReasonOf("sig1", new BuildState("A", "sig1", LastResult: BuildResult.Succeeded)));

    /// <summary>Hollow ve kapsam-dışı hâllerde gerekçe YOKTUR: ilkinde bilinmiyor, ikincisinde üyelik
    /// kanalı (döngü rozeti) zaten konuşuyor — plan gerekçesi orada yanıltıcı olurdu.</summary>
    [Fact]
    public void hollow_and_out_of_cycle_scope_carry_no_reason()
    {
        Assert.Null(ReasonOf(null, null));
        Assert.Null(ReasonOf("sig1", null, inCycle: true));
    }

    /// <summary>Evaluate, EvaluateWithReason'a delege eder — iki yüzey ayrışamaz (kopya yok).</summary>
    [Fact]
    public void the_two_surfaces_always_agree()
    {
        var state = new BuildState("A", "sig1", LastResult: BuildResult.Succeeded, DepIssue: true);
        Assert.Equal(WillBuildEvaluator.Evaluate(false, "sig1", state, buildCycles: false),
                     WillBuildEvaluator.EvaluateWithReason(false, "sig1", state, buildCycles: false).WillBuild);
    }

    [Fact]
    public void ComputeWillBuild_populates_node_field()
    {
        var node = new ProjectNode("A", "A", "A", [], [], 0, null, null, InCycle: false, WillBuild: null);
        var plan = new BuildPlan([node], [], "Debug");
        var result = BuildPreview.ComputeWillBuild(plan, _ => "sig1", _ => null, buildCycles: false); // never built
        Assert.True(result.Nodes[0].WillBuild);
    }
}
