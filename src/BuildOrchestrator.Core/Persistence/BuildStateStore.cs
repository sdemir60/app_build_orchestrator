using System.Collections.Concurrent;
using BuildOrchestrator.Contracts;
using BuildOrchestrator.Core.Configuration;

namespace BuildOrchestrator.Core.Persistence;

/// <summary>
/// Persists per-project build state keyed by (projectId, branch) (Section 6 / build-state.json).
/// Thread-safe so the parallel build engine can update results concurrently.
/// </summary>
public sealed class BuildStateStore
{
    private readonly ConcurrentDictionary<string, BuildState> _states = new();

    public BuildStateStore()
    {
        Load();
    }

    private static string Key(string projectId, string branch) => $"{branch}::{projectId}";

    public BuildState? Get(string projectId, string branch)
        => _states.TryGetValue(Key(projectId, branch), out var s) ? s : null;

    public void Set(BuildState state)
        => _states[Key(state.ProjectId, state.Branch)] = state;

    public void Load()
    {
        AppPaths.EnsureRoot();
        var list = JsonStore.Load<List<BuildState>>(AppPaths.BuildStateFile);
        if (list is null)
            return;
        _states.Clear();
        foreach (var s in list)
            _states[Key(s.ProjectId, s.Branch)] = s;
    }

    public void Save()
    {
        AppPaths.EnsureRoot();
        JsonStore.Save(AppPaths.BuildStateFile, _states.Values.ToList());
    }
}
