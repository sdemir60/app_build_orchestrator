using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Storage;

/// <summary>Serializable snapshot of the dependency graph cached to <c>dependency-graph.json</c> (Section 5).</summary>
public sealed class GraphCache
{
    public string RootPath { get; set; } = string.Empty;
    public DateTimeOffset BuiltAt { get; set; }
    public bool HasCycles { get; set; }
    public List<ProjectNode> Projects { get; set; } = new();
}

/// <summary>Persists the cached dependency graph so build order is not recomputed every run (Section 5).</summary>
public sealed class GraphCacheStore
{
    private readonly AppPaths _paths;
    private readonly JsonStore _store;

    public GraphCacheStore(AppPaths paths, JsonStore store)
    {
        _paths = paths;
        _store = store;
    }

    public GraphCache? Load() => _store.Read<GraphCache>(_paths.DependencyGraphFile);

    public void Save(GraphCache cache) => _store.Write(_paths.DependencyGraphFile, cache);
}
