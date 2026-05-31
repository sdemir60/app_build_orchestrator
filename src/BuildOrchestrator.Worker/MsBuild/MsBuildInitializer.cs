using Microsoft.Build.Locator;

namespace BuildOrchestrator.Worker.MsBuild;

/// <summary>
/// Locates the machine's MSBuild via <see cref="MSBuildLocator"/> (Section 2). Must be called once,
/// before any type that references the Microsoft.Build APIs is JIT-compiled.
/// </summary>
public static class MsBuildInitializer
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static bool IsRegistered => _registered;

    public static void EnsureRegistered()
    {
        if (_registered)
        {
            return;
        }

        lock (Gate)
        {
            if (_registered)
            {
                return;
            }

            if (!MSBuildLocator.IsRegistered)
            {
                var instance = MSBuildLocator.QueryVisualStudioInstances()
                    .OrderByDescending(i => i.Version)
                    .FirstOrDefault();

                if (instance is not null)
                {
                    MSBuildLocator.RegisterInstance(instance);
                }
                else
                {
                    MSBuildLocator.RegisterDefaults();
                }
            }

            _registered = true;
        }
    }
}
