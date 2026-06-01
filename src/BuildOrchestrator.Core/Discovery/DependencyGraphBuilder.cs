using System.Xml.Linq;
using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Discovery;

/// <summary>
/// Builds the global dependency graph from ProjectReference edges (Section 5):
/// nodes, topological order, and SCC-based cycle detection.
/// </summary>
public sealed class DependencyGraphBuilder
{
    public DependencyGraph Build(string rootPath, ScanResult scan, Action<string, int, int>? progress = null)
    {
        // 1. Create a node per discovered project.
        var byId = new Dictionary<string, MutableNode>();
        var pathToId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var projectPath in scan.ProjectFiles)
        {
            var normalized = ProjectId.Normalize(projectPath);
            var id = ProjectId.FromPath(projectPath);
            pathToId[normalized] = id;
            scan.ProjectToSolution.TryGetValue(normalized, out var solution);
            byId[id] = new MutableNode
            {
                Id = id,
                Name = Path.GetFileNameWithoutExtension(projectPath),
                ProjectPath = Path.GetFullPath(projectPath),
                SolutionName = solution ?? string.Empty
            };
        }

        // 2. Parse ProjectReference edges (only to projects we discovered).
        int index = 0;
        int total = scan.ProjectFiles.Count;
        foreach (var projectPath in scan.ProjectFiles)
        {
            index++;
            progress?.Invoke(Path.GetFileName(projectPath), index, total);

            var fromId = pathToId[ProjectId.Normalize(projectPath)];
            foreach (var refPath in ParseProjectReferences(projectPath))
            {
                var key = ProjectId.Normalize(refPath);
                if (pathToId.TryGetValue(key, out var toId) && toId != fromId)
                    byId[fromId].Dependencies.Add(toId);
            }
        }

        // 3. Tarjan SCC for cycle detection + condensation order.
        var scc = new TarjanScc(byId);
        var components = scc.Compute();

        // 4. Topologically order the condensation, then expand to projects.
        var order = TopologicalOrder(byId, components);

        // 5. Materialize immutable ProjectNodes.
        var orderIndex = new Dictionary<string, int>();
        for (int i = 0; i < order.Count; i++)
            orderIndex[order[i]] = i;

        var projects = new List<ProjectNode>(byId.Count);
        foreach (var node in byId.Values)
        {
            var comp = scc.ComponentOf[node.Id];
            var members = components[comp];
            bool inCycle = members.Count > 1;
            projects.Add(new ProjectNode
            {
                Id = node.Id,
                Name = node.Name,
                ProjectPath = node.ProjectPath,
                SolutionName = node.SolutionName,
                Dependencies = node.Dependencies.ToList(),
                BuildOrder = orderIndex.TryGetValue(node.Id, out var bo) ? bo : 0,
                InCycle = inCycle,
                CycleMembers = inCycle
                    ? members.Where(m => m != node.Id).Select(m => byId[m].Name).ToList()
                    : new List<string>()
            });
        }

        projects.Sort((a, b) => a.BuildOrder.CompareTo(b.BuildOrder));

        return new DependencyGraph
        {
            RootPath = Path.GetFullPath(rootPath),
            GeneratedAt = DateTimeOffset.UtcNow,
            Projects = projects,
            TopologicalOrder = order
        };
    }

    private static IEnumerable<string> ParseProjectReferences(string projectPath)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Load(projectPath);
        }
        catch
        {
            yield break;
        }

        var dir = Path.GetDirectoryName(projectPath)!;
        foreach (var pr in doc.Descendants().Where(e => e.Name.LocalName == "ProjectReference"))
        {
            var include = pr.Attribute("Include")?.Value;
            if (string.IsNullOrWhiteSpace(include))
                continue;
            var rel = include.Replace('\\', Path.DirectorySeparatorChar);
            string full;
            try
            {
                full = Path.GetFullPath(Path.Combine(dir, rel));
            }
            catch
            {
                continue;
            }
            yield return full;
        }
    }

    /// <summary>
    /// Order SCC components topologically (dependencies first), then expand each
    /// component's members (cycle members ordered by name for determinism).
    /// </summary>
    private static List<string> TopologicalOrder(
        Dictionary<string, MutableNode> byId,
        List<List<string>> components)
    {
        // Build condensation edges: component -> set of dependency components.
        var compOf = new Dictionary<string, int>();
        for (int c = 0; c < components.Count; c++)
            foreach (var m in components[c])
                compOf[m] = c;

        var compDeps = new Dictionary<int, HashSet<int>>();
        var indegree = new int[components.Count];
        for (int c = 0; c < components.Count; c++)
            compDeps[c] = new HashSet<int>();

        for (int c = 0; c < components.Count; c++)
        {
            foreach (var member in components[c])
            {
                foreach (var dep in byId[member].Dependencies)
                {
                    var dc = compOf[dep];
                    if (dc != c && compDeps[c].Add(dc))
                        indegree[c]++;
                }
            }
        }

        // Kahn over the condensation; dependencies (indegree 0) emitted first.
        var queue = new PriorityQueue<int, string>();
        for (int c = 0; c < components.Count; c++)
            if (indegree[c] == 0)
                queue.Enqueue(c, ComponentKey(components[c], byId));

        // reverse adjacency: who depends on c
        var dependents = new Dictionary<int, List<int>>();
        for (int c = 0; c < components.Count; c++)
            dependents[c] = new List<int>();
        for (int c = 0; c < components.Count; c++)
            foreach (var dc in compDeps[c])
                dependents[dc].Add(c);

        var ordered = new List<string>();
        while (queue.Count > 0)
        {
            var c = queue.Dequeue();
            foreach (var member in components[c].OrderBy(m => byId[m].Name, StringComparer.OrdinalIgnoreCase))
                ordered.Add(member);

            foreach (var d in dependents[c])
            {
                if (--indegree[d] == 0)
                    queue.Enqueue(d, ComponentKey(components[d], byId));
            }
        }

        // Safety: if anything was left out (shouldn't happen), append it.
        if (ordered.Count < byId.Count)
            foreach (var id in byId.Keys)
                if (!ordered.Contains(id))
                    ordered.Add(id);

        return ordered;
    }

    private static string ComponentKey(List<string> members, Dictionary<string, MutableNode> byId)
        => members.Select(m => byId[m].Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).First();

    private sealed class MutableNode
    {
        public required string Id { get; init; }
        public required string Name { get; init; }
        public required string ProjectPath { get; init; }
        public string SolutionName { get; init; } = string.Empty;
        public HashSet<string> Dependencies { get; } = new();
    }

    /// <summary>Tarjan's strongly-connected-components algorithm.</summary>
    private sealed class TarjanScc
    {
        private readonly Dictionary<string, MutableNode> _nodes;
        private readonly Dictionary<string, int> _indices = new();
        private readonly Dictionary<string, int> _lowlink = new();
        private readonly HashSet<string> _onStack = new();
        private readonly Stack<string> _stack = new();
        private int _index;

        public List<List<string>> Result { get; } = new();
        public Dictionary<string, int> ComponentOf { get; } = new();

        public TarjanScc(Dictionary<string, MutableNode> nodes) => _nodes = nodes;

        public List<List<string>> Compute()
        {
            foreach (var id in _nodes.Keys)
                if (!_indices.ContainsKey(id))
                    StrongConnect(id);

            for (int c = 0; c < Result.Count; c++)
                foreach (var m in Result[c])
                    ComponentOf[m] = c;

            return Result;
        }

        private void StrongConnect(string v)
        {
            // Iterative Tarjan to avoid stack overflow on deep graphs.
            var callStack = new Stack<(string node, IEnumerator<string> deps)>();
            _indices[v] = _lowlink[v] = _index++;
            _stack.Push(v);
            _onStack.Add(v);
            callStack.Push((v, _nodes[v].Dependencies.GetEnumerator()));

            while (callStack.Count > 0)
            {
                var (node, deps) = callStack.Peek();
                bool recursed = false;
                while (deps.MoveNext())
                {
                    var w = deps.Current;
                    if (!_nodes.ContainsKey(w))
                        continue;
                    if (!_indices.ContainsKey(w))
                    {
                        _indices[w] = _lowlink[w] = _index++;
                        _stack.Push(w);
                        _onStack.Add(w);
                        callStack.Push((w, _nodes[w].Dependencies.GetEnumerator()));
                        recursed = true;
                        break;
                    }
                    if (_onStack.Contains(w))
                        _lowlink[node] = Math.Min(_lowlink[node], _indices[w]);
                }

                if (recursed)
                    continue;

                if (_lowlink[node] == _indices[node])
                {
                    var component = new List<string>();
                    string w;
                    do
                    {
                        w = _stack.Pop();
                        _onStack.Remove(w);
                        component.Add(w);
                    } while (w != node);
                    Result.Add(component);
                }

                callStack.Pop();
                if (callStack.Count > 0)
                {
                    var parent = callStack.Peek().node;
                    _lowlink[parent] = Math.Min(_lowlink[parent], _lowlink[node]);
                }
            }
        }
    }
}
