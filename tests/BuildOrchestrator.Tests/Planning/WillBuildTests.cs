using BuildOrchestrator.Core.Planning;
using BuildOrchestrator.Contracts.Model;

namespace BuildOrchestrator.Tests.Planning;

// [T53][A6][v7Δ-8] WillBuildEvaluator karar tablosu + BuildPreview.ComputeWillBuild mapping testleri.
// It-1 kapsamı: currentSignature enjekte edilen bir provider (gerçek imza motoru T25, It-3'te bağlanır).
public class WillBuildTests
{
    [Fact]
    public void hollow_when_signature_null()
        => Assert.Null(WillBuildEvaluator.Evaluate(false, null, null));

    [Fact]
    public void true_when_never_built()
        => Assert.True(WillBuildEvaluator.Evaluate(false, "sig1", null));

    [Fact]
    public void true_when_dirty()
        => Assert.True(WillBuildEvaluator.Evaluate(false,
            "sig2", new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded)));

    [Fact]
    public void false_when_up_to_date_after_success() // succeeded → clean
        => Assert.False(WillBuildEvaluator.Evaluate(false,
            "sig1", new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Succeeded)));

    [Fact]
    public void false_when_in_cycle_regardless()
        => Assert.False(WillBuildEvaluator.Evaluate(true, "sig1", null));

    [Fact]
    public void true_when_last_result_failed_even_if_signature_matches()
        => Assert.True(WillBuildEvaluator.Evaluate(false,
            "sig1", new BuildState("A", BuiltSignature: "sig1", LastResult: BuildResult.Failed)));

    [Fact]
    public void ComputeWillBuild_populates_node_field()
    {
        var node = new ProjectNode("A", "A", "A", [], [], 0, null, null, InCycle: false, WillBuild: null);
        var plan = new BuildPlan([node], [], "Debug");
        var result = BuildPreview.ComputeWillBuild(plan, _ => "sig1", _ => null); // never built
        Assert.True(result.Nodes[0].WillBuild);
    }
}
