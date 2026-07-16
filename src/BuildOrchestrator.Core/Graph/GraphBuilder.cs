namespace BuildOrchestrator.Core.Graph;

using BuildOrchestrator.Core.Discovery;

/// <summary>
/// Bir projenin (deduped, sıralı) dependency edge'leri.
/// </summary>
public sealed record ProjectEdges(string ProjectId, IReadOnlyList<string> Dependencies);

/// <summary>
/// Dependency graph edge builder. Kenar primeri (D11): HintPath basename → producer eşlemesi.
/// ProjectReference İKİNCİL sinyaldir; HintPath ile aynı kenarı üretiyorsa dedup edilir.
/// </summary>
public static class GraphBuilder
{
    public static IReadOnlyList<ProjectEdges> BuildEdges(IReadOnlyList<EvaluatedProject> projects, ProducerMap producers)
    {
        var byId = projects.ToDictionary(p => p.Path, StringComparer.OrdinalIgnoreCase);
        var result = new List<ProjectEdges>();

        // Determinizm [D8]: proje sırası OrdinalIgnoreCase Path'e göre.
        foreach (var p in projects.OrderBy(p => p.Path, StringComparer.OrdinalIgnoreCase))
        {
            var deps = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);

            // PRİMER: HintPath basename → producer [D11]
            foreach (var h in p.HintPaths)
                if (producers.DllToProducer.TryGetValue(h.BaseName, out var prod) &&
                    !prod.Equals(p.Path, StringComparison.OrdinalIgnoreCase))
                    deps.Add(prod);

            // İKİNCİL: ProjectReference — yalnız bilinen bir projeye çözülüyorsa; HintPath edge'iyle dedup (SortedSet)
            foreach (var pr in p.ProjectReferences)
                if (byId.ContainsKey(pr) && !pr.Equals(p.Path, StringComparison.OrdinalIgnoreCase))
                    deps.Add(pr);

            result.Add(new ProjectEdges(p.Path, deps.ToList()));
        }

        return result;
    }
}
