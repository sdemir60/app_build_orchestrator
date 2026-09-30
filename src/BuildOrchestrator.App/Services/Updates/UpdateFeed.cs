using System.IO;
using Velopack.Sources;

namespace BuildOrchestrator.App.Services.Updates;

/// <summary>[motor] Güncelleme beslemesinin TEK yeri: GitHub Releases (public repo, token gerekmez). Dev/test kapıları
/// ortam değişkeninden (mevcut <c>BO_*</c> deseni): <see cref="SourceOverrideVariable"/> bir klasör (yerel feed —
/// GitHub'sız uçtan uca prova) ya da URL; <see cref="PrereleaseVariable"/>=1 prerelease'leri de görür (aşamalı yayın:
/// <c>vpk upload github --pre</c>). UI'da ayar yoktur.</summary>
public static class UpdateFeed
{
    public const string RepositoryUrl = "https://github.com/sdemir60/app_build_orchestrator";
    public const string SourceOverrideVariable = "BO_UPDATE_SOURCE";
    public const string PrereleaseVariable = "BO_UPDATE_PRERELEASE";

    /// <summary>Ölçüm testlerinin kapısıyla aynı kural: yalnız tam olarak "1".</summary>
    public static bool IsEnabled(string? flag) => flag == "1";

    public static IUpdateSource CreateSource(string? overrideSource, bool prerelease)
    {
        if (string.IsNullOrWhiteSpace(overrideSource)) return new GithubSource(RepositoryUrl, accessToken: null, prerelease);
        if (overrideSource.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || overrideSource.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return overrideSource.Contains("github.com/", StringComparison.OrdinalIgnoreCase)
                ? new GithubSource(overrideSource, accessToken: null, prerelease)
                : new SimpleWebSource(overrideSource);
        }
        return new SimpleFileSource(new DirectoryInfo(overrideSource)); // UpdateManager(string) de böyle çözer
    }
}
