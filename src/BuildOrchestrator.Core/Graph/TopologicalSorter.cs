using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Graph;

/// <summary>
/// Produces a topological build order via Kahn's algorithm and groups independent
/// projects into parallel "waves" (Section 6 parallelism). Cyclic nodes are appended
/// deterministically at the end so the run can still proceed.
/// </summary>
public static class TopologicalSorter
{
    public sealed record Result(
        IReadOnlyList<string> Order,
        IReadOnlyList<IReadOnlyList<string>> Waves,
        IReadOnlyList<string> CyclicRemainder);

    public static Result Sort(DependencyGraph graph)
    {
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in graph.Nodes)
        {
            inDegree[node.Id] = 0;
        }

        foreach (var node in graph.Nodes)
        {
            foreach (var dep in graph.DependenciesOf(node.Id))
            {
                // edge dep -> node ; node depends on dep, so node's in-degree increases.
                inDegree[node.Id]++;
            }
        }

        var dependents = graph.BuildDependents();
        var order = new List<string>(graph.Count);
        var waves = new List<IReadOnlyList<string>>();

        // Seed wave: nodes with no dependencies, sorted for deterministic output.
        var current = inDegree.Where(kv => kv.Value == 0)
            .Select(kv => kv.Key)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        var processed = new HashSet<string>(StringComparer.Ordinal);

        while (current.Count > 0)
        {
            waves.Add(current);
            var next = new List<string>();

            foreach (var id in current)
            {
                order.Add(id);
                processed.Add(id);

                foreach (var dependent in dependents[id])
                {
                    if (--inDegree[dependent] == 0)
                    {
                        next.Add(dependent);
                    }
                }
            }

            next.Sort(StringComparer.Ordinal);
            current = next;
        }

        // Any node not processed is part of (or downstream of) a cycle.
        var remainder = graph.Nodes
            .Select(n => n.Id)
            .Where(id => !processed.Contains(id))
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToList();

        if (remainder.Count > 0)
        {
            order.AddRange(remainder);
            waves.Add(remainder);
        }

        return new Result(order, waves, remainder);
    }

    /// <summary>Writes the computed order back into each node's <see cref="ProjectNode.BuildOrder"/>.</summary>
    public static void ApplyBuildOrder(IEnumerable<ProjectNode> nodes, Result result)
    {
        var rank = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < result.Order.Count; i++)
        {
            rank[result.Order[i]] = i;
        }

        foreach (var node in nodes)
        {
            node.BuildOrder = rank.TryGetValue(node.Id, out var r) ? r : int.MaxValue;
        }
    }
}
