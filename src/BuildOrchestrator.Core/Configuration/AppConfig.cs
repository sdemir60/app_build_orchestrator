using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Configuration;

/// <summary>
/// User configuration (Section 3). Persisted as config.json under
/// %LOCALAPPDATA%\BuildOrchestrator\.
/// </summary>
public sealed class AppConfig
{
    public string? RootPath { get; set; }
    public BuildConfiguration BuildConfiguration { get; set; } = BuildConfiguration.Debug;
    public PerformanceMode Performance { get; set; } = PerformanceMode.FullPower;
    public BranchWorkMode BranchWorkMode { get; set; } = BranchWorkMode.Worktree;
    public LogLevel LogLevel { get; set; } = LogLevel.ErrorsOnly;
    public DependentMode DependentMode { get; set; } = DependentMode.Safe;

    /// <summary>Optional override for the cache root. Null = default AppPaths.Root.</summary>
    public string? CacheLocation { get; set; }

    public bool ReducedMotion { get; set; }
    public bool AutoStart { get; set; }
    public bool MinimizeToTray { get; set; } = true;

    public AppConfig Clone() => (AppConfig)MemberwiseClone();
}
