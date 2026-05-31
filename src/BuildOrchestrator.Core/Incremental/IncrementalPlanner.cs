using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Graph;

namespace BuildOrchestrator.Core.Incremental;

/// <summary>Why a project was selected to build, for logging/console reasons.</summary>
public enum BuildReason
{
    NeverBuilt,
    CommitChanged,
    LocalChange,
    DependentOfDirty,
    Rebuild
}

/// <summary>Per-project decision produced by the planner.</summary>
public sealed record ProjectDecision(string ProjectId, bool ShouldBuild, BuildReason? Reason);

/// <summary>Inputs needed to decide an incremental plan (Section 6).</summary>
public sealed class IncrementalContext
{
    public required string Branch { get; init; }

    /// <summary>Current HEAD commit on the branch being built.</summary>
    public required string CurrentCommit { get; init; }

    /// <summary>Ids of projects with working-tree changes affecting them (from <see cref="ProjectFileMapper"/>).</summary>
    public required IReadOnlySet<string> LocallyDirty { get; init; }

    /// <summary>Lookup of last successful build state per project on this branch.</summary>
    public required Func<string, BuildState?> StateLookup { get; init; }

    public DependentMode DependentMode { get; init; } = DependentMode.Safe;
}

/// <summary>
/// Computes the incremental build plan. A project builds when its commit moved, when it has a
/// local change, or when it was never successfully built. In Safe mode the transitive dependents
/// of every selected project are also built; in Fast mode only the directly-dirty projects build.
/// </summary>
public sealed class IncrementalPlanner
{
    private readonly DependencyGraph _graph;

    public IncrementalPlanner(DependencyGraph graph)
    {
        _graph = graph;
    }

    public IReadOnlyList<ProjectDecision> PlanBuild(IncrementalContext context)
    {
        var reasons = new Dictionary<string, BuildReason>(StringComparer.Ordinal);

        foreach (var node in _graph.Nodes)
        {
            var state = context.StateLookup(node.Id);

            if (state is null || state.LastResult != ProjectStatus.Succeeded || string.IsNullOrEmpty(state.LastBuiltCommit))
            {
                reasons[node.Id] = BuildReason.NeverBuilt;
            }
            else if (!string.Equals(state.LastBuiltCommit, context.CurrentCommit, StringComparison.Ordinal))
            {
                reasons[node.Id] = BuildReason.CommitChanged;
            }
            else if (context.LocallyDirty.Contains(node.Id))
            {
                reasons[node.Id] = BuildReason.LocalChange;
            }
        }

        if (context.DependentMode == DependentMode.Safe && reasons.Count > 0)
        {
            var dependents = _graph.TransitiveDependents(reasons.Keys);
            foreach (var id in dependents)
            {
                if (!reasons.ContainsKey(id))
                {
                    reasons[id] = BuildReason.DependentOfDirty;
                }
            }
        }

        return _graph.Nodes
            .OrderBy(n => n.BuildOrder)
            .Select(n => reasons.TryGetValue(n.Id, out var r)
                ? new ProjectDecision(n.Id, true, r)
                : new ProjectDecision(n.Id, false, null))
            .ToList();
    }

    /// <summary>Rebuild: every project builds, in topological order (Section 6 Rebuild).</summary>
    public IReadOnlyList<ProjectDecision> PlanRebuild()
        => _graph.Nodes
            .OrderBy(n => n.BuildOrder)
            .Select(n => new ProjectDecision(n.Id, true, BuildReason.Rebuild))
            .ToList();
}
