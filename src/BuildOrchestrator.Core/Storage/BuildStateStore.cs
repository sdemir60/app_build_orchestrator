using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Storage;

/// <summary>
/// Persists per (project, branch) <see cref="BuildState"/> in <c>build-state.json</c> (Section 6).
/// Keyed by projectId + branch so each branch tracks its own last-built commit.
/// </summary>
public sealed class BuildStateStore
{
    private readonly AppPaths _paths;
    private readonly JsonStore _store;
    private readonly object _gate = new();
    private Dictionary<string, BuildState> _states;

    public BuildStateStore(AppPaths paths, JsonStore store)
    {
        _paths = paths;
        _store = store;
        _states = LoadAll();
    }

    private Dictionary<string, BuildState> LoadAll()
    {
        var list = _store.Read<List<BuildState>>(_paths.BuildStateFile) ?? new List<BuildState>();
        return list.ToDictionary(Key, StringComparer.Ordinal);
    }

    private static string Key(BuildState s) => Key(s.ProjectId, s.Branch);

    public static string Key(string projectId, string branch) => $"{projectId}\u0000{branch}";

    public BuildState? Get(string projectId, string branch)
    {
        lock (_gate)
        {
            return _states.TryGetValue(Key(projectId, branch), out var s) ? s : null;
        }
    }

    public void Set(BuildState state)
    {
        lock (_gate)
        {
            _states[Key(state)] = state;
            Persist();
        }
    }

    public void SetMany(IEnumerable<BuildState> states)
    {
        lock (_gate)
        {
            foreach (var s in states)
            {
                _states[Key(s)] = s;
            }
            Persist();
        }
    }

    public IReadOnlyCollection<BuildState> All()
    {
        lock (_gate)
        {
            return _states.Values.ToList();
        }
    }

    private void Persist() => _store.Write(_paths.BuildStateFile, _states.Values.ToList());
}
