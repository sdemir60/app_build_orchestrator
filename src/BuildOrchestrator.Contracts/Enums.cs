namespace BuildOrchestrator.Contracts;

/// <summary>
/// Lifecycle/visual states for a project card (Section 7).
/// </summary>
public enum ProjectStatus
{
    Discovered,
    Queued,
    Building,
    Succeeded,
    Failed,
    Skipped,
    CycleDetected
}

/// <summary>Build mode requested by the user.</summary>
public enum BuildMode
{
    Build,
    Rebuild
}

/// <summary>MSBuild configuration.</summary>
public enum BuildConfiguration
{
    Debug,
    Release
}

/// <summary>Downstream (dependent) propagation mode (Section 6).</summary>
public enum DependentMode
{
    /// <summary>Dirty + transitive dependents are rebuilt.</summary>
    Safe,
    /// <summary>Only the dirty projects themselves are rebuilt.</summary>
    Fast
}

/// <summary>Performance mode controlling parallel degree + process priority (Section 3).</summary>
public enum PerformanceMode
{
    FullPower,
    Balanced,
    Light
}

/// <summary>Branch working mode (Section 3).</summary>
public enum BranchMode
{
    /// <summary>Prepare branch in an isolated worktree pool (default).</summary>
    Worktree,
    /// <summary>Checkout into the existing working directory.</summary>
    InPlaceCheckout
}

/// <summary>Console/log verbosity (Section 3).</summary>
public enum LogLevel
{
    ErrorsOnly,
    Full
}
