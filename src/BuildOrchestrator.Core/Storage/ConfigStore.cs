using BuildOrchestrator.Core.Configuration;

namespace BuildOrchestrator.Core.Storage;

/// <summary>Loads/saves <see cref="AppConfig"/> from <see cref="AppPaths.ConfigFile"/>.</summary>
public sealed class ConfigStore
{
    private readonly AppPaths _paths;
    private readonly JsonStore _store;

    public ConfigStore(AppPaths paths, JsonStore store)
    {
        _paths = paths;
        _store = store;
    }

    public AppConfig Load() => _store.Read<AppConfig>(_paths.ConfigFile) ?? new AppConfig();

    public void Save(AppConfig config) => _store.Write(_paths.ConfigFile, config);
}
