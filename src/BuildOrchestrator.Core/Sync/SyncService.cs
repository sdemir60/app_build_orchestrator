using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Graph;
using BuildOrchestrator.Core.Storage;

namespace BuildOrchestrator.Core.Sync;

/// <summary>
/// Orchestrates Section 5: full re-analysis (Sync) writes the dependency graph cache; normal startup
/// reads the cache instead of recomputing. The build order is only recomputed on explicit Sync.
/// </summary>
public sealed class SyncService
{
    private readonly WorkspaceScanner _scanner;
    private readonly GraphCacheStore _cacheStore;

    public SyncService(WorkspaceScanner scanner, GraphCacheStore cacheStore)
    {
        _scanner = scanner;
        _cacheStore = cacheStore;
    }

    /// <summary>Full re-analysis: scan, build graph, persist cache, return the result.</summary>
    public ScanResult Reanalyze(string rootPath, SyncProgressHandler? progress = null, CancellationToken ct = default)
    {
        var result = _scanner.Scan(rootPath, progress, ct);
        _cacheStore.Save(new GraphCache
        {
            RootPath = rootPath,
            BuiltAt = DateTimeOffset.UtcNow,
            HasCycles = result.HasCycles,
            Projects = result.Projects.ToList()
        });
        return result;
    }

    /// <summary>
    /// Loads the cached graph for <paramref name="rootPath"/> if present and valid, otherwise null
    /// (caller should trigger <see cref="Reanalyze"/>).
    /// </summary>
    public DependencyGraph? LoadCachedGraph(string rootPath, out GraphCache? cache)
    {
        cache = _cacheStore.Load();
        if (cache is null || !PathUtil.IdEquals(cache.RootPath, rootPath) || cache.Projects.Count == 0)
        {
            cache = null;
            return null;
        }
        return new DependencyGraph(cache.Projects);
    }
}
