using BuildOrchestrator.Core.Configuration;

namespace BuildOrchestrator.Core.Persistence;

/// <summary>Loads and saves <see cref="AppConfig"/> from %LOCALAPPDATA%.</summary>
public sealed class ConfigStore
{
    public AppConfig Load()
    {
        AppPaths.EnsureRoot();
        return JsonStore.Load<AppConfig>(AppPaths.ConfigFile) ?? new AppConfig();
    }

    public void Save(AppConfig config)
    {
        AppPaths.EnsureRoot();
        JsonStore.Save(AppPaths.ConfigFile, config);
    }
}
