namespace BuildOrchestrator.Contracts;

/// <summary>A node in the global dependency graph (Section 8 type).</summary>
public sealed record ProjectNode
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ProjectPath { get; init; }
    public string SolutionName { get; init; } = string.Empty;

    /// <summary>Project ids this project directly depends on (ProjectReference).</summary>
    public List<string> Dependencies { get; init; } = new();

    /// <summary>Topological build order index (0 = no dependencies).</summary>
    public int BuildOrder { get; init; }

    /// <summary>True when the project participates in a dependency cycle (SCC size &gt; 1).</summary>
    public bool InCycle { get; init; }

    /// <summary>Other project ids in the same cycle (for tooltip display).</summary>
    public List<string> CycleMembers { get; init; } = new();
}

/// <summary>Persisted per-project build state (Section 6 / build-state.json).</summary>
public sealed record BuildState
{
    public required string ProjectId { get; init; }
    public string Branch { get; init; } = string.Empty;
    public string? LastBuiltCommit { get; init; }
    public ProjectStatus LastResult { get; init; } = ProjectStatus.Discovered;
    public DateTimeOffset? LastRunAt { get; init; }
}

/// <summary>A request to start a run (Section 8 type).</summary>
public sealed record RunRequest
{
    public required BuildMode Mode { get; init; }
    public required string Branch { get; init; }
    public BuildConfiguration Config { get; init; } = BuildConfiguration.Debug;
    public DependentMode DependentMode { get; init; } = DependentMode.Safe;
    public PerformanceMode Performance { get; init; } = PerformanceMode.FullPower;
    public BranchWorkMode BranchWorkMode { get; init; } = BranchWorkMode.Worktree;
}

/// <summary>The full dependency graph cached to dependency-graph.json.</summary>
public sealed record DependencyGraph
{
    public required string RootPath { get; init; }
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<ProjectNode> Projects { get; init; } = new();

    /// <summary>Topologically ordered project ids (cycles broken deterministically).</summary>
    public List<string> TopologicalOrder { get; init; } = new();
}

public sealed record BranchInfo
{
    public required string Name { get; init; }
    public bool IsCurrent { get; init; }
}
