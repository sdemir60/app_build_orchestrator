using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Graph;

/// <summary>
/// In-memory dependency graph over <see cref="ProjectNode"/>s plus derived analyses:
/// topological order, strongly-connected components (cycles), and transitive dependents.
/// </summary>
public sealed class DependencyGraph
{
    private readonly Dictionary<string, ProjectNode> _nodes;

    public DependencyGraph(IEnumerable<ProjectNode> nodes)
    {
        _nodes = nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
    }

    public IReadOnlyCollection<ProjectNode> Nodes => _nodes.Values;

    public int Count => _nodes.Count;

    public bool TryGet(string id, out ProjectNode node) => _nodes.TryGetValue(id, out node!);

    /// <summary>Direct dependency ids that actually exist in the graph.</summary>
    public IEnumerable<string> DependenciesOf(string id)
        => _nodes.TryGetValue(id, out var n)
            ? n.Dependencies.Where(_nodes.ContainsKey)
            : Enumerable.Empty<string>();

    /// <summary>
    /// Build a reverse adjacency map: dependency id -> set of project ids that depend on it.
    /// </summary>
    public Dictionary<string, HashSet<string>> BuildDependents()
    {
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var id in _nodes.Keys)
        {
            dependents[id] = new HashSet<string>(StringComparer.Ordinal);
        }

        foreach (var node in _nodes.Values)
        {
            foreach (var dep in node.Dependencies)
            {
                if (dependents.TryGetValue(dep, out var set))
                {
                    set.Add(node.Id);
                }
            }
        }

        return dependents;
    }

    /// <summary>
    /// Transitive closure of dependents for the given seed ids (Section 6 Safe mode).
    /// Seeds are included in the result.
    /// </summary>
    public HashSet<string> TransitiveDependents(IEnumerable<string> seeds)
    {
        var dependents = BuildDependents();
        var result = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();

        foreach (var seed in seeds)
        {
            if (_nodes.ContainsKey(seed) && result.Add(seed))
            {
                stack.Push(seed);
            }
        }

        while (stack.Count > 0)
        {
            var current = stack.Pop();
            if (!dependents.TryGetValue(current, out var set))
            {
                continue;
            }

            foreach (var d in set)
            {
                if (result.Add(d))
                {
                    stack.Push(d);
                }
            }
        }

        return result;
    }
}
