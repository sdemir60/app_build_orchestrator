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

    [Fact]
    public void true_when_signature_matches_but_last_result_null()
        => Assert.True(WillBuildEvaluator.Evaluate(false,
            "sig1", new BuildState("A", BuiltSignature: "sig1", LastResult: null), buildCycles: false));

    [Fact]
    public void ComputeWillBuild_populates_node_field()
    {
        var node = new ProjectNode("A", "A", "A", [], [], 0, null, null, InCycle: false, WillBuild: null);
        var plan = new BuildPlan([node], [], "Debug");
        var result = BuildPreview.ComputeWillBuild(plan, _ => "sig1", _ => null, buildCycles: false); // never built
        Assert.True(result.Nodes[0].WillBuild);
    }
}
