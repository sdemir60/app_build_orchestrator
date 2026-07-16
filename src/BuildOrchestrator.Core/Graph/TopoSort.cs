namespace BuildOrchestrator.Core.Graph;

/// <summary>
/// Topological sort sonucu. BuildOrder: bağımlılıklar önce, deterministik.
/// Cycles: her biri >1 üyeli SCC (strongly connected component), üyeler ordinal sıralı.
/// </summary>
public sealed record TopoResult(IReadOnlyList<string> BuildOrder, IReadOnlyList<IReadOnlyList<string>> Cycles);

/// <summary>
/// Tarjan SCC (cycle detection) + Kahn topological sort (condensation DAG üzerinde).
/// Determinizm [D8]: bağ kırma SCC'nin min projectId'sine (OrdinalIgnoreCase) göre;
/// SCC içi üyeler projectId ordinal sıralı; bağımsız düğümler projectId sırasıyla çıkar.
/// </summary>
public static class TopoSort
{
    public static TopoResult Compute(IReadOnlyList<ProjectEdges> edges)
    {
        var adj = edges.ToDictionary(e => e.ProjectId, e => e.Dependencies, StringComparer.OrdinalIgnoreCase);
        var nodes = adj.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();

        // --- Tarjan SCC ---
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var low = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var onStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        int idx = 0;
        var sccs = new List<List<string>>();
        var sccOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        void StrongConnect(string v)
        {
            index[v] = low[v] = idx++; stack.Push(v); onStack.Add(v);
            foreach (var w in Neighbors(v))
            {
                if (!index.ContainsKey(w)) { StrongConnect(w); low[v] = Math.Min(low[v], low[w]); }
                else if (onStack.Contains(w)) low[v] = Math.Min(low[v], index[w]);
            }
            if (low[v] == index[v])
            {
                var comp = new List<string>();
                string w;
                do { w = stack.Pop(); onStack.Remove(w); comp.Add(w); } while (!w.Equals(v, StringComparison.OrdinalIgnoreCase));
                comp.Sort(StringComparer.OrdinalIgnoreCase);
                int id = sccs.Count; sccs.Add(comp);
                foreach (var m in comp) sccOf[m] = id;
            }
        }
        IEnumerable<string> Neighbors(string v) =>
            (adj.TryGetValue(v, out var d) ? d : []).Where(adj.ContainsKey)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase);

        foreach (var n in nodes) if (!index.ContainsKey(n)) StrongConnect(n);

        // --- condensation DAG + Kahn (bağ kırma: SCC min id) ---
        int k = sccs.Count;
        var sccDeps = new HashSet<int>[k];
        for (int i = 0; i < k; i++) sccDeps[i] = new();
        foreach (var (v, deps) in adj)
            foreach (var w in deps)
                if (adj.ContainsKey(w) && sccOf[v] != sccOf[w]) sccDeps[sccOf[v]].Add(sccOf[w]);

        var indeg = new int[k];
        for (int i = 0; i < k; i++) foreach (var _ in sccDeps[i]) indeg[i]++; // i, bağımlılıklarına derece verir
        // build-order: bağımlılık önce ⇒ derecesi (bağımlı olduğu SCC sayısı) 0 olanlar önce
        var ready = new SortedSet<int>(Comparer<int>.Create((x, y) =>
            string.Compare(sccs[x][0], sccs[y][0], StringComparison.OrdinalIgnoreCase)));
        for (int i = 0; i < k; i++) if (indeg[i] == 0) ready.Add(i);
        var dependents = new List<int>[k]; // ters kenar: kim bana bağımlı
        for (int i = 0; i < k; i++) dependents[i] = new();
        for (int i = 0; i < k; i++) foreach (var d in sccDeps[i]) dependents[d].Add(i);

        var order = new List<string>();
        while (ready.Count > 0)
        {
            int cur = ready.Min; ready.Remove(cur);
            order.AddRange(sccs[cur]); // SCC üyeleri (sıralı) ardışık
            foreach (var dep in dependents[cur].OrderBy(x => x))
                if (--indeg[dep] == 0) ready.Add(dep);
        }

        var cycles = sccs.Where(s => s.Count > 1).Select(s => (IReadOnlyList<string>)s).ToList();
        return new TopoResult(order, cycles);
    }
}
