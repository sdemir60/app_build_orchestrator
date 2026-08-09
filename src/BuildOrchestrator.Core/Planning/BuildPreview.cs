namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;

/// <summary>
/// [T53][A6][v7Δ-8] Bir BuildPlan'daki her ProjectNode için WillBuildEvaluator kararını uygular ve
/// WillBuild alanı dolu YENİ bir BuildPlan döner (kaynak plan değiştirilmez).
/// </summary>
public static class BuildPreview
{
    public static BuildPlan ComputeWillBuild(BuildPlan plan,
        Func<ProjectNode, string?> currentSignature, Func<string, BuildState?> stateLookup, bool buildCycles)
    {
        var nodes = plan.Nodes.Select(n => n with
        {
            WillBuild = WillBuildEvaluator.Evaluate(n.InCycle, currentSignature(n), stateLookup(n.Id), buildCycles)
        }).ToList();
        return plan with { Nodes = nodes };
    }
}
