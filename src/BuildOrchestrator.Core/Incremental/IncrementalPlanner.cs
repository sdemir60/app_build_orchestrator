using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Persistence;

namespace BuildOrchestrator.Core.Incremental;

public enum BuildDecision
{
    Build,
    Skip
}

public sealed record ProjectPlan(string ProjectId, BuildDecision Decision, string Reason);

public sealed class RunPlan
{
    public List<ProjectPlan> Items { get; } = new();
    public IEnumerable<string> ToBuild => Items.Where(i => i.Decision == BuildDecision.Build).Select(i => i.ProjectId);
    public IEnumerable<string> ToSkip => Items.Where(i => i.Decision == BuildDecision.Skip).Select(i => i.ProjectId);
}

/// <summary>
/// Computes the incremental build plan (Section 6): which projects build vs skip, honoring
/// commit/diff/never-built signals and Safe/Fast downstream propagation.
/// </summary>
public sealed class IncrementalPlanner
{
    /// <summary>
    /// Build a project when: (1) current commit differs from last built commit, OR
    /// (2) a local change affects it, OR (3) it was never successfully built.
    /// Safe mode additionally pulls in transitive dependents of dirty projects.
    /// </summary>
    public RunPlan Plan(
        DependencyGraph graph,
        BuildStateStore states,
        string branch,
        string? currentCommit,
        ISet<string> locallyDirty,
        DependentMode dependentMode)
    {
        var directlyDirty = new HashSet<string>();

        foreach (var project in graph.Projects)
        {
            var state = states.Get(project.Id, branch);

            bool neverBuilt = state is null || state.LastResult != ProjectStatus.Succeeded;
            bool commitChanged = state?.LastBuiltCommit is null ||
                                 !string.Equals(state.LastBuiltCommit, currentCommit, StringComparison.Ordinal);
            bool localChange = locallyDirty.Contains(project.Id);

            if (neverBuilt || localChange || (commitChanged && currentCommit != null))
                directlyDirty.Add(project.Id);
        }

        var toBuild = new HashSet<string>(directlyDirty);

        if (dependentMode == DependentMode.Safe)
            PropagateToDependents(graph, directlyDirty, toBuild);

        var plan = new RunPlan();
        foreach (var project in graph.Projects.OrderBy(p => p.BuildOrder))
        {
            if (toBuild.Contains(project.Id))
            {
                var reason = directlyDirty.Contains(project.Id)
                    ? ReasonFor(project, states, branch, currentCommit, locallyDirty)
                    : "dependent of changed project";
                plan.Items.Add(new ProjectPlan(project.Id, BuildDecision.Build, reason));
            }
            else
            {
                plan.Items.Add(new ProjectPlan(project.Id, BuildDecision.Skip, "no source change"));
            }
        }
        return plan;
    }

    private static string ReasonFor(
        ProjectNode project, BuildStateStore states, string branch,
        string? currentCommit, ISet<string> locallyDirty)
    {
        var state = states.Get(project.Id, branch);
        if (state is null || state.LastResult != ProjectStatus.Succeeded)
            return "never built";
        if (locallyDirty.Contains(project.Id))
            return "local changes";
        if (!string.Equals(state.LastBuiltCommit, currentCommit, StringComparison.Ordinal))
            return "new commit";
        return "changed";
    }

    /// <summary>Add transitive dependents (reverse edges) of all dirty projects.</summary>
    private static void PropagateToDependents(
        DependencyGraph graph, HashSet<string> dirty, HashSet<string> result)
    {
        // Reverse adjacency: dependency -> list of projects that depend on it.
        var dependents = new Dictionary<string, List<string>>();
        foreach (var p in graph.Projects)
            foreach (var dep in p.Dependencies)
            {
                if (!dependents.TryGetValue(dep, out var list))
                    dependents[dep] = list = new List<string>();
                list.Add(p.Id);
            }

        var queue = new Queue<string>(dirty);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (!dependents.TryGetValue(cur, out var deps))
                continue;
            foreach (var d in deps)
                if (result.Add(d))
                    queue.Enqueue(d);
        }
    }
}
