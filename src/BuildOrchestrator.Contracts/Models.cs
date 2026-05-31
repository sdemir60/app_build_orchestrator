namespace BuildOrchestrator.Contracts;

/// <summary>
/// A node in the global dependency graph (Section 8).
/// </summary>
public sealed class ProjectNode
{
    /// <summary>Stable identifier (normalized absolute project path).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Project display name (file name without extension).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Absolute path to the .csproj file.</summary>
    public string ProjectPath { get; set; } = string.Empty;

    /// <summary>Name of the solution that contains the project (if any).</summary>
    public string SolutionName { get; set; } = string.Empty;

    /// <summary>Ids of projects this project directly depends on (ProjectReference).</summary>
    public List<string> Dependencies { get; set; } = new();

    /// <summary>Topological build order index. Lower builds earlier.</summary>
    public int BuildOrder { get; set; }

    /// <summary>True if this node participates in a dependency cycle.</summary>
    public bool IsInCycle { get; set; }

    /// <summary>Ids of the projects that form the cycle this node belongs to (for tooltip).</summary>
    public List<string> CycleMembers { get; set; } = new();
}

/// <summary>
/// Persisted per-project build state used for incremental decisions (Section 6).
/// </summary>
public sealed class BuildState
{
    public string ProjectId { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;

    /// <summary>Commit hash that was last built successfully on this branch.</summary>
    public string? LastBuiltCommit { get; set; }

    public ProjectStatus LastResult { get; set; } = ProjectStatus.Discovered;
    public DateTimeOffset? LastRunAt { get; set; }
}

/// <summary>
/// Request payload to start a run (Section 8).
/// </summary>
public sealed class RunRequest
{
    public BuildMode Mode { get; set; } = BuildMode.Build;
    public string Branch { get; set; } = string.Empty;
    public BuildConfiguration Config { get; set; } = BuildConfiguration.Debug;
    public DependentMode DependentMode { get; set; } = DependentMode.Safe;
    public PerformanceMode Performance { get; set; } = PerformanceMode.FullPower;
}
