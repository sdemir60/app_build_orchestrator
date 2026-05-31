using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Storage;

namespace BuildOrchestrator.Core.Tests;

public class StorageTests : IDisposable
{
    private readonly string _root;
    private readonly AppPaths _paths;
    private readonly JsonStore _store;

    public StorageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "bo_store_" + Guid.NewGuid().ToString("N"));
        _paths = new AppPaths(_root);
        _store = new JsonStore();
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* ignore */ }
    }

    [Fact]
    public void BuildStateStore_RoundTripsPerBranch()
    {
        var s = new BuildStateStore(_paths, _store);
        s.Set(new BuildState { ProjectId = "P1", Branch = "main", LastBuiltCommit = "abc", LastResult = ProjectStatus.Succeeded });
        s.Set(new BuildState { ProjectId = "P1", Branch = "dev", LastBuiltCommit = "def", LastResult = ProjectStatus.Failed });

        var reloaded = new BuildStateStore(_paths, _store);
        Assert.Equal("abc", reloaded.Get("P1", "main")!.LastBuiltCommit);
        Assert.Equal("def", reloaded.Get("P1", "dev")!.LastBuiltCommit);
        Assert.Null(reloaded.Get("P1", "missing"));
    }

    [Fact]
    public void JsonStore_AtomicWrite_OverwritesExisting()
    {
        var path = _paths.ConfigFile;
        _store.Write(path, new { Value = 1 });
        _store.Write(path, new { Value = 2 });

        var roundtrip = _store.Read<Dictionary<string, int>>(path);
        Assert.Equal(2, roundtrip!["value"]);
    }

    [Fact]
    public void AppPaths_SanitizesBranchNamesForWorktrees()
    {
        var wt = _paths.WorktreeFor("feature/login");
        Assert.DoesNotContain("/feature/login", wt);
        Assert.Contains("feature_login", wt);
    }
}
