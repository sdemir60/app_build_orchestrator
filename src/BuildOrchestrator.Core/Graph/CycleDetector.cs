using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Graph;

/// <summary>
/// Detects strongly-connected components (cycles) using Tarjan's algorithm.
/// Any SCC with more than one member — or a single node that references itself —
/// is a dependency cycle (Section 5: circular dependency via SCC).
/// </summary>
public static class CycleDetector
{
    public sealed record Result(IReadOnlyList<IReadOnlyList<string>> Cycles)
    {
        public bool HasCycles => Cycles.Count > 0;
    }

    public static Result FindCycles(DependencyGraph graph)
    {
        var index = 0;
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowlink = new Dictionary<string, int>(StringComparer.Ordinal);
        var cycles = new List<IReadOnlyList<string>>();

        // Iterative Tarjan to avoid stack overflow on large/deep graphs (500-1000+ nodes).
        foreach (var node in graph.Nodes)
        {
            if (indices.ContainsKey(node.Id))
            {
                continue;
            }

            StrongConnect(node.Id);
        }

        return new Result(cycles);

        void StrongConnect(string root)
        {
            var work = new Stack<(string Node, IEnumerator<string> Edges)>();

            void Open(string n)
            {
                indices[n] = index;
                lowlink[n] = index;
                index++;
                stack.Push(n);
                onStack.Add(n);
                work.Push((n, graph.DependenciesOf(n).GetEnumerator()));
            }

            Open(root);

            while (work.Count > 0)
            {
                var (v, edges) = work.Peek();

                if (edges.MoveNext())
                {
                    var w = edges.Current;
                    if (!indices.ContainsKey(w))
                    {
                        Open(w);
                    }
                    else if (onStack.Contains(w))
                    {
                        lowlink[v] = Math.Min(lowlink[v], indices[w]);
                    }
                }
                else
                {
                    // Done exploring v: propagate lowlink up to parent and maybe emit an SCC.
                    work.Pop();
                    if (work.Count > 0)
                    {
                        var parent = work.Peek().Node;
                        lowlink[parent] = Math.Min(lowlink[parent], lowlink[v]);
                    }

                    if (lowlink[v] == indices[v])
                    {
                        var scc = new List<string>();
                        string w;
                        do
                        {
                            w = stack.Pop();
                            onStack.Remove(w);
                            scc.Add(w);
                        }
                        while (!string.Equals(w, v, StringComparison.Ordinal));

                        if (scc.Count > 1 || ReferencesSelf(graph, scc[0]))
                        {
                            cycles.Add(scc);
                        }
                    }
                }
            }
        }
    }

    private static bool ReferencesSelf(DependencyGraph graph, string id)
        => graph.DependenciesOf(id).Contains(id);

    /// <summary>
    /// Annotates nodes that participate in a cycle with <see cref="ProjectNode.IsInCycle"/>
    /// and the member list for tooltips.
    /// </summary>
    public static void Annotate(IEnumerable<ProjectNode> nodes, Result result)
    {
        var byId = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
        foreach (var cycle in result.Cycles)
        {
            foreach (var id in cycle)
            {
                if (byId.TryGetValue(id, out var node))
                {
                    node.IsInCycle = true;
                    node.CycleMembers = cycle.ToList();
                }
            }
        }
    }
}
