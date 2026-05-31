using BuildOrchestrator.Contracts;

namespace BuildOrchestrator.Core.Configuration;

/// <summary>
/// User configuration persisted as JSON (Section 3). Defaults match Section 11.
/// </summary>
public sealed class AppConfig
{
    /// <summary>Root directory scanned for solutions/projects.</summary>
    public string RootPath { get; set; } = string.Empty;

    public BuildConfiguration Configuration { get; set; } = BuildConfiguration.Debug;

    public PerformanceMode Performance { get; set; } = PerformanceMode.FullPower;

    public BranchMode BranchMode { get; set; } = BranchMode.Worktree;

    public LogLevel LogLevel { get; set; } = LogLevel.ErrorsOnly;

    public DependentMode DependentMode { get; set; } = DependentMode.Safe;

    /// <summary>Cache/data location; defaults to %LOCALAPPDATA%\BuildOrchestrator.</summary>
    public string? CacheLocation { get; set; }

    /// <summary>When true, animations are minimized (Section 7). Default off.</summary>
    public bool ReducedMotion { get; set; }

    /// <summary>Launch with Windows via the Run registry key (opt-in).</summary>
    public bool AutoStart { get; set; }

    /// <summary>Max console ring-buffer line count.</summary>
    public int ConsoleMaxLines { get; set; } = 20000;

    public AppConfig Clone() => (AppConfig)MemberwiseClone();
}
