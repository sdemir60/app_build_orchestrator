namespace BuildOrchestrator.Core.Planning;

using BuildOrchestrator.Contracts.Model;
using BuildOrchestrator.Core.Incremental;

/// <summary>
/// [T53][A6][v7Δ-8] Bir BuildPlan'daki her ProjectNode için WillBuildEvaluator kararını uygular ve
/// WillBuild alanı dolu YENİ bir BuildPlan döner (kaynak plan değiştirilmez).
/// [Faz 3 — spec 2026-09-18 §5] <c>outputOf</c> projenin çıktı kanıtı kontrolünü değerlendiriciye YALNIZ aktarır;
/// verilmezse (ya da proje için <c>null</c> ise) karar bugünküdür.
/// </summary>
public static class BuildPreview
{
    public static BuildPlan ComputeWillBuild(BuildPlan plan,
        Func<ProjectNode, string?> currentSignature, Func<string, BuildState?> stateLookup, bool buildCycles,
        Func<string, OutputCheck?>? outputOf = null)
    {
        var nodes = plan.Nodes.Select(n =>
        {
            var (willBuild, reason) =
                WillBuildEvaluator.EvaluateWithReason(
                    n.InCycle, currentSignature(n), stateLookup(n.Id), buildCycles, outputOf?.Invoke(n.Id));
            return n with { WillBuild = willBuild, WillBuildReason = reason };
        }).ToList();
        return plan with { Nodes = nodes };
    }
}
