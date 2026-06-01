using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Configuration;

namespace BuildOrchestrator.Core.Persistence;

/// <summary>Persists the cached dependency graph (dependency-graph.json, Section 5).</summary>
public sealed class GraphStore
{
    public DependencyGraph? Load()
    {
        AppPaths.EnsureRoot();
        return JsonStore.Load<DependencyGraph>(AppPaths.DependencyGraphFile);
    }

    public void Save(DependencyGraph graph)
    {
        AppPaths.EnsureRoot();
        JsonStore.Save(AppPaths.DependencyGraphFile, graph);
    }
}
